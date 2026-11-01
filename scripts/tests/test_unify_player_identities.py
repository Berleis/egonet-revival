import importlib.util
import json
from pathlib import Path
import sqlite3
import unittest

spec = importlib.util.spec_from_file_location('repair', Path(__file__).parents[1] / 'unify-player-identities.py')
repair = importlib.util.module_from_spec(spec)
spec.loader.exec_module(repair)


class IdentityRepairTests(unittest.TestCase):
    def setUp(self):
        self.db = sqlite3.connect(':memory:')
        self.db.execute('PRAGMA foreign_keys=ON')
        self.db.executescript('''
            CREATE TABLE PlayerProfiles (Id INTEGER PRIMARY KEY, ExternalId TEXT UNIQUE, DisplayName TEXT);
            CREATE TABLE Dirt4CommunityState (Id INTEGER PRIMARY KEY, StateJson TEXT, UpdatedAt TEXT);
            CREATE TABLE RaceNetSessions (Id INTEGER PRIMARY KEY, PlayerProfileId INTEGER REFERENCES PlayerProfiles(Id));
            CREATE TABLE Grid2GlobalScores (Id INTEGER PRIMARY KEY, PlayerProfileId INTEGER REFERENCES PlayerProfiles(Id), Score INTEGER);
            CREATE TABLE Ghosts (Id INTEGER PRIMARY KEY, OwnerPlayerProfileId INTEGER REFERENCES PlayerProfiles(Id), Data BLOB);
            INSERT INTO PlayerProfiles VALUES (1, 'name:old', 'Old'), (2, 'steam:123', 'New');
            INSERT INTO RaceNetSessions VALUES (1, 1);
            INSERT INTO Grid2GlobalScores VALUES (1, 1, 42);
            INSERT INTO Ghosts VALUES (1, 1, X'010203');
        ''')
        self.state = {
            'CommunityEvents': [{'EventId': 12, 'Runs': {'name:old': {
                'NetworkId': 123, 'TimeMs': 12345, 'ResultDelivery': 2,
                'Stages': [{'TimeMs': 12345}]}}}],
            'StandaloneLeaderboards': [{'LeaderboardId': 99, 'Runs': {'name:old': {'TimeMs': 42}}}],
            'Presences': {'name:old': {'NetworkId': 123, 'Name': 'Old', 'BoundToPlayer': True}},
            'UnrelatedField': [1, 2, 3]
        }
        self.write_state()

    def tearDown(self):
        self.db.close()

    def write_state(self):
        self.db.execute('INSERT OR REPLACE INTO Dirt4CommunityState VALUES (1, ?, ?)', (json.dumps(self.state), 'before'))
        self.db.commit()

    def apply(self, **options):
        self.db.execute('BEGIN IMMEDIATE')
        try:
            result = repair.unify(self.db, 1, 2, **options)
            self.db.commit()
            return result
        except Exception:
            self.db.rollback()
            raise

    def test_moves_all_game_references_preserving_delivery_and_history(self):
        self.assertEqual([(1, 2)], repair.candidates(self.db))
        self.apply()
        updated = json.loads(self.db.execute('SELECT StateJson FROM Dirt4CommunityState').fetchone()[0])
        self.assertEqual(self.state['CommunityEvents'][0]['Runs']['name:old'], updated['CommunityEvents'][0]['Runs']['steam:123'])
        self.assertEqual([1, 2, 3], updated['UnrelatedField'])
        self.assertEqual((2, 42), self.db.execute('SELECT PlayerProfileId,Score FROM Grid2GlobalScores').fetchone())
        self.assertEqual((2, b'\x01\x02\x03'), self.db.execute('SELECT OwnerPlayerProfileId,Data FROM Ghosts').fetchone())
        self.assertEqual(2, self.db.execute('SELECT PlayerProfileId FROM RaceNetSessions').fetchone()[0])
        self.assertEqual(2, self.db.execute('SELECT count(*) FROM PlayerProfiles').fetchone()[0])
        self.assertEqual([], self.db.execute('PRAGMA foreign_key_check').fetchall())

    def test_idempotent(self):
        self.apply()
        before = self.db.execute('SELECT StateJson FROM Dirt4CommunityState').fetchone()[0]
        self.assertEqual(0, self.apply()['moved_json_entries'])
        self.assertEqual(before, self.db.execute('SELECT StateJson FROM Dirt4CommunityState').fetchone()[0])

    def test_conflicting_results_roll_back_everything(self):
        self.state['CommunityEvents'][0]['Runs']['steam:123'] = {'TimeMs': 50}
        self.write_state()
        with self.assertRaises(ValueError):
            self.apply()
        self.assertEqual(1, self.db.execute('SELECT PlayerProfileId FROM RaceNetSessions').fetchone()[0])
        self.assertEqual(json.dumps(self.state), self.db.execute('SELECT StateJson FROM Dirt4CommunityState').fetchone()[0])

    def test_unique_constraint_collision_is_not_resolved_by_deleting_scores(self):
        self.db.executescript('CREATE UNIQUE INDEX single_score ON Grid2GlobalScores(PlayerProfileId); INSERT INTO Grid2GlobalScores VALUES (2,2,99);')
        with self.assertRaises(sqlite3.IntegrityError):
            self.apply()
        self.assertEqual([(1, 42), (2, 99)], self.db.execute('SELECT PlayerProfileId,Score FROM Grid2GlobalScores ORDER BY Id').fetchall())
        self.assertEqual(1, self.db.execute('SELECT PlayerProfileId FROM RaceNetSessions').fetchone()[0])

    def test_unbound_or_conflicting_network_does_not_merge(self):
        self.state['Presences']['name:old']['BoundToPlayer'] = False
        self.write_state()
        self.assertEqual([], repair.candidates(self.db))
        with self.assertRaises(ValueError):
            self.apply()

    def test_pending_stays_pending(self):
        self.state['CommunityEvents'][0]['Runs']['name:old']['ResultDelivery'] = 1
        self.write_state()
        self.apply()
        updated = json.loads(self.db.execute('SELECT StateJson FROM Dirt4CommunityState').fetchone()[0])
        self.assertEqual(1, updated['CommunityEvents'][0]['Runs']['steam:123']['ResultDelivery'])

    def test_third_namesake_is_not_silently_merged(self):
        self.db.execute('INSERT INTO PlayerProfiles VALUES (3, ?, ?)', (repair.name_key('New'), 'New'))
        self.db.commit()
        self.assertEqual([3], self.apply()['skipped_name_alias_owners'])
        self.assertEqual(3, self.db.execute('SELECT count(*) FROM PlayerProfiles').fetchone()[0])
        self.assertIsNone(self.db.execute('SELECT * FROM PlayerIdentityAliases WHERE ExternalId=?', (repair.name_key('New'),)).fetchone())

    def test_identical_friendships_are_consolidated_without_removing_other_friends(self):
        self.db.executescript('''
            INSERT INTO PlayerProfiles VALUES (3, 'steam:456', 'Friend');
            CREATE TABLE PlayerFriends (Id INTEGER PRIMARY KEY,
                PlayerProfileId INTEGER REFERENCES PlayerProfiles(Id),
                FriendPlayerProfileId INTEGER REFERENCES PlayerProfiles(Id), SteamId TEXT,
                UNIQUE(PlayerProfileId,SteamId));
            INSERT INTO PlayerFriends VALUES (1,1,3,'456'), (2,2,3,'456');
        ''')
        self.assertEqual(1, self.apply()['duplicate_friend_links'])
        self.assertEqual([(2, 3, '456')], self.db.execute('SELECT PlayerProfileId,FriendPlayerProfileId,SteamId FROM PlayerFriends').fetchall())

    def test_friend_pointer_repair_does_not_reassign_owned_data(self):
        self.db.executescript('''
            ALTER TABLE PlayerProfiles ADD COLUMN FirstSeenAt TEXT;
            ALTER TABLE PlayerProfiles ADD COLUMN LastSeenAt TEXT;
            CREATE TABLE PlayerFriends (Id INTEGER PRIMARY KEY,
                PlayerProfileId INTEGER REFERENCES PlayerProfiles(Id),
                FriendPlayerProfileId INTEGER REFERENCES PlayerProfiles(Id), SteamId TEXT,
                DisplayName TEXT, LastSeenAt TEXT);
            INSERT INTO PlayerFriends VALUES (1,1,2,'456','Friend','2026-11-01');
        ''')
        result = repair.repair_friend_links(self.db)
        self.assertEqual({'repaired_friend_links': 1, 'created_friend_profiles': 1}, result)
        self.assertEqual('steam:123', self.db.execute('SELECT ExternalId FROM PlayerProfiles WHERE Id=2').fetchone()[0])
        self.assertEqual('steam:456', self.db.execute('SELECT p.ExternalId FROM PlayerProfiles p JOIN PlayerFriends f ON p.Id=f.FriendPlayerProfileId').fetchone()[0])
        self.assertEqual((1, 42), self.db.execute('SELECT PlayerProfileId,Score FROM Grid2GlobalScores').fetchone())
        self.assertEqual(0, repair.repair_friend_links(self.db)['repaired_friend_links'])

    def grid2_history(self):
        self.db.executescript('''
            INSERT INTO PlayerProfiles VALUES (3, 'steam:456', 'Rival');
            CREATE TABLE Grid2RivalSessionData (Id INTEGER PRIMARY KEY,
                PlayerProfileId INTEGER UNIQUE REFERENCES PlayerProfiles(Id), SessionData BLOB, UpdatedAt TEXT);
            INSERT INTO Grid2RivalSessionData VALUES
                (1,1,X'0102','2020-01-01T00:00:00+00:00'), (2,2,X'0304','2020-01-02T00:00:00+00:00');
            CREATE TABLE Grid2RivalAssignments (Id INTEGER PRIMARY KEY,
                PlayerProfileId INTEGER REFERENCES PlayerProfiles(Id),
                RivalPlayerProfileId INTEGER REFERENCES PlayerProfiles(Id), Type INTEGER,
                StartsAt TEXT, ExpiresAt TEXT, CreatedAt TEXT, UNIQUE(PlayerProfileId,StartsAt,Type));
            INSERT INTO Grid2RivalAssignments VALUES
                (1,1,3,0,'2020-01-01','2020-01-08T00:00:00+00:00','2020-01-01T00:00:00+00:00'),
                (2,2,1,0,'2020-01-01','2020-01-08T00:00:00+00:00','2020-01-02T00:00:00+00:00'),
                (3,1,3,1,'2020-01-01','2020-01-08T00:00:00+00:00','2020-01-01T00:00:00+00:00'),
                (4,2,3,1,'2020-01-01','2020-01-08T00:00:00+00:00','2020-01-02T00:00:00+00:00');
        ''')

    def test_grid2_history_archives_originals_and_keeps_latest_session(self):
        self.grid2_history()
        report = self.apply(reconcile_grid2_history=True)
        self.assertEqual(3, report['archived_grid2_records'])
        self.assertEqual([(2, b'\x03\x04')], self.db.execute('SELECT PlayerProfileId,SessionData FROM Grid2RivalSessionData').fetchall())
        archived = json.loads(self.db.execute("SELECT RowJson FROM PlayerIdentityRepairArchive WHERE TableName='Grid2RivalSessionData'").fetchone()[0])
        self.assertEqual({'base64': 'AQI='}, archived['SessionData'])
        self.assertEqual(1, archived['PlayerProfileId'])
        self.assertEqual([(1, 2, 3), (4, 2, 3)], self.db.execute('SELECT Id,PlayerProfileId,RivalPlayerProfileId FROM Grid2RivalAssignments ORDER BY Id').fetchall())
        self.assertEqual((2, 42), self.db.execute('SELECT PlayerProfileId,Score FROM Grid2GlobalScores').fetchone())
        self.assertEqual(0, self.apply(reconcile_grid2_history=True)['archived_grid2_records'])

    def test_grid2_history_requires_explicit_option(self):
        self.grid2_history()
        with self.assertRaises(sqlite3.IntegrityError):
            self.apply()

    def test_grid2_active_assignment_conflict_rolls_back_snapshot_archival(self):
        self.grid2_history()
        self.db.execute("UPDATE Grid2RivalAssignments SET ExpiresAt='9999-01-01T00:00:00+00:00'")
        self.db.commit()
        with self.assertRaisesRegex(ValueError, 'active'):
            self.apply(reconcile_grid2_history=True)
        self.assertEqual(2, self.db.execute('SELECT count(*) FROM Grid2RivalSessionData').fetchone()[0])
        self.assertEqual(4, self.db.execute('SELECT count(*) FROM Grid2RivalAssignments').fetchone()[0])
        self.assertIsNone(self.db.execute("SELECT 1 FROM sqlite_master WHERE name='PlayerIdentityRepairArchive'").fetchone())

    def test_grid2_equal_snapshot_times_with_different_data_abort(self):
        self.grid2_history()
        self.db.execute("UPDATE Grid2RivalSessionData SET UpdatedAt='2020-01-01T00:00:00+00:00'")
        self.db.commit()
        with self.assertRaisesRegex(ValueError, 'same timestamp'):
            self.apply(reconcile_grid2_history=True)

    def test_grid2_newer_source_snapshot_is_retained(self):
        self.grid2_history()
        self.db.execute("UPDATE Grid2RivalSessionData SET UpdatedAt='2020-01-03T00:00:00+00:00' WHERE PlayerProfileId=1")
        self.db.commit()
        self.apply(reconcile_grid2_history=True)
        self.assertEqual((2, b'\x01\x02'), self.db.execute('SELECT PlayerProfileId,SessionData FROM Grid2RivalSessionData').fetchone())


if __name__ == '__main__':
    unittest.main()
