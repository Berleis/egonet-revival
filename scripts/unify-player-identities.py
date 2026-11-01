#!/usr/bin/env python3
"""Offline, operator-reviewed SQLite identity repair. Default: dry run on a memory copy.

Usage: python3 scripts/unify-player-identities.py DATABASE --pair OLD_ID:STEAM_PROFILE_ID
Apply only with the server stopped, --apply --server-stopped --backup-directory DIRECTORY.
Aliases are durable links, NOT authentication. Never select accounts by display name alone.
"""
import argparse
import datetime
import hashlib
import json
import os
from pathlib import Path
import sqlite3


def quote(identifier):
    return '"' + identifier.replace('"', '""') + '"'


def name_key(name):
    return 'name:' + hashlib.sha256(name.strip().lower().encode('utf-8')).hexdigest()


def state_rows(db):
    if not db.execute("SELECT 1 FROM sqlite_master WHERE name='Dirt4CommunityState'").fetchone():
        return []
    return db.execute('SELECT Id, StateJson FROM Dirt4CommunityState').fetchall()


def candidates(db):
    profiles = {row[1]: row[0] for row in db.execute('SELECT Id, ExternalId FROM PlayerProfiles')}
    result = set()
    for _, raw in state_rows(db):
        state = json.loads(raw)
        if not isinstance(state, dict):
            continue
        for key, presence in (state.get('Presences') or {}).items():
            network = presence.get('NetworkId')
            if not key.startswith('name:') or not network or not presence.get('BoundToPlayer'):
                continue
            observed = {run['NetworkId'] for event in state.get('CommunityEvents') or []
                        for player, run in event['Runs'].items()
                        if player == key and run.get('NetworkId')}
            target = 'steam:' + str(network)
            if observed == {network} and key in profiles and target in profiles:
                result.add((profiles[key], profiles[target]))
    return sorted(result)


def move_key(mapping, source, target):
    if source not in mapping:
        return 0
    if target in mapping and mapping[target] != mapping[source]:
        raise ValueError('Conflicting results at the destination; manual review required')
    mapping[target] = mapping.pop(source)
    return 1


def unify(db, source_id, target_id):
    if source_id == target_id:
        raise ValueError('Source and target must differ')
    source = db.execute('SELECT ExternalId, DisplayName FROM PlayerProfiles WHERE Id=?', (source_id,)).fetchone()
    target = db.execute('SELECT ExternalId, DisplayName FROM PlayerProfiles WHERE Id=?', (target_id,)).fetchone()
    if not source or not target or not source[0].startswith('name:') or not target[0].startswith('steam:'):
        raise ValueError('Expected an existing name-only source and Steam target')
    network = int(target[0][6:])
    if network <= 0:
        raise ValueError('Invalid target Steam identity')
    report = {'source_profile': source_id, 'target_profile': target_id, 'moved_json_entries': 0,
              'references': {}, 'skipped_name_alias_owners': [], 'duplicate_friend_links': 0}
    db.execute('CREATE TABLE IF NOT EXISTS PlayerIdentityAliases ('
               'ExternalId TEXT NOT NULL PRIMARY KEY, '
               'PlayerProfileId INTEGER NOT NULL REFERENCES PlayerProfiles(Id) ON DELETE RESTRICT)')
    for alias in {source[0], name_key(source[1]), name_key(target[1])}:
        prior = db.execute('SELECT PlayerProfileId FROM PlayerIdentityAliases WHERE ExternalId=?', (alias,)).fetchone()
        owner = db.execute('SELECT Id FROM PlayerProfiles WHERE ExternalId=?', (alias,)).fetchone()
        if (prior and prior[0] != target_id) or (owner and owner[0] not in (source_id, target_id)):
            if alias != source[0]:
                report['skipped_name_alias_owners'].append(prior[0] if prior else owner[0])
                continue
            raise ValueError('Alias already belongs to another profile')
        db.execute('INSERT OR IGNORE INTO PlayerIdentityAliases VALUES (?, ?)', (alias, target_id))

    for row_id, raw in state_rows(db):
        state = json.loads(raw)
        events = state if isinstance(state, list) else (state.get('CommunityEvents') or [])
        moved = 0
        for event in events:
            run = event['Runs'].get(source[0])
            if run and run.get('NetworkId') not in (None, 0, network):
                raise ValueError('A source result belongs to a different Steam identity')
            moved += move_key(event['Runs'], source[0], target[0])
        if isinstance(state, dict):
            for board in state.get('StandaloneLeaderboards') or []:
                moved += move_key(board['Runs'], source[0], target[0])
            presences = state.get('Presences') or {}
            old = presences.get(source[0])
            if old:
                if not old.get('BoundToPlayer') or old.get('NetworkId') != network:
                    raise ValueError('Source presence is not consistently bound to target Steam identity')
                current = presences.get(target[0])
                if current and (not current.get('BoundToPlayer') or current.get('NetworkId') != network):
                    raise ValueError('Conflicting destination presence')
                presences[target[0]] = dict(current or old, Name=target[1])
                del presences[source[0]]
                moved += 1
        if moved:
            db.execute('UPDATE Dirt4CommunityState SET StateJson=?, UpdatedAt=? WHERE Id=?',
                       (json.dumps(state, ensure_ascii=False), datetime.datetime.now(datetime.timezone.utc).isoformat(), row_id))
        report['moved_json_entries'] += moved

    # Discover all FK references, including tables introduced by newer game versions.
    # Unique-key collisions abort the entire transaction rather than dropping either record.
    tables = [r[0] for r in db.execute("SELECT name FROM sqlite_master WHERE type='table'")]
    if 'PlayerFriends' in tables:
        duplicates = db.execute('''SELECT a.Id, a.FriendPlayerProfileId, b.FriendPlayerProfileId
            FROM PlayerFriends a JOIN PlayerFriends b ON a.SteamId=b.SteamId
            WHERE a.PlayerProfileId=? AND b.PlayerProfileId=?''', (source_id, target_id)).fetchall()
        for row_id, old_friend, new_friend in duplicates:
            if (target_id if old_friend == source_id else old_friend) != (target_id if new_friend == source_id else new_friend):
                raise ValueError('Duplicate friend links disagree about the friend identity')
            # Same owner, Steam ID and friend profile: this is one relationship, not a score.
            db.execute('DELETE FROM PlayerFriends WHERE Id=?', (row_id,))
            report['duplicate_friend_links'] += 1
    for table in tables:
        columns = [r[3] for r in db.execute('PRAGMA foreign_key_list(' + quote(table) + ')') if r[2] == 'PlayerProfiles']
        for column in columns:
            count = db.execute(f'UPDATE {quote(table)} SET {quote(column)}=? WHERE {quote(column)}=?',
                               (target_id, source_id)).rowcount
            if count:
                report['references'][table + '.' + column] = count
    # Source profile is retained as an alias/audit record; no historical scores are deleted.
    if db.execute('PRAGMA foreign_key_check').fetchall():
        raise ValueError('Foreign-key check failed')
    return report


def repair_friend_links(db):
    """Repair a relationship's explicit Steam ID, never move the friend's owned data."""
    report = {'repaired_friend_links': 0, 'created_friend_profiles': 0}
    if not db.execute("SELECT 1 FROM sqlite_master WHERE name='PlayerFriends'").fetchone():
        return report
    rows = db.execute('''SELECT f.Id, f.SteamId, f.DisplayName, f.LastSeenAt
        FROM PlayerFriends f JOIN PlayerProfiles p ON p.Id=f.FriendPlayerProfileId
        WHERE p.ExternalId LIKE 'steam:%' AND substr(p.ExternalId,7)<>f.SteamId''').fetchall()
    for row_id, steam_id, name, last_seen in rows:
        if not steam_id.isascii() or not steam_id.isdecimal() or int(steam_id) <= 0 or str(int(steam_id)) != steam_id:
            raise ValueError('Invalid Steam ID on a conflicting friend link')
        external = 'steam:' + steam_id
        target = db.execute('SELECT Id FROM PlayerProfiles WHERE ExternalId=?', (external,)).fetchone()
        if target is None:
            cursor = db.execute('INSERT INTO PlayerProfiles (ExternalId,DisplayName,FirstSeenAt,LastSeenAt) VALUES (?,?,?,?)',
                                (external, name, last_seen, last_seen))
            target = (cursor.lastrowid,)
            report['created_friend_profiles'] += 1
        db.execute('UPDATE PlayerFriends SET FriendPlayerProfileId=? WHERE Id=?', (target[0], row_id))
        report['repaired_friend_links'] += 1
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('database', type=Path)
    parser.add_argument('--pair', action='append', default=[], help='Reviewed source:target profile IDs')
    parser.add_argument('--apply', action='store_true')
    parser.add_argument('--repair-friend-links', action='store_true', help='Repair mismatched friend pointers by explicit Steam ID, without moving scores')
    parser.add_argument('--server-stopped', action='store_true')
    parser.add_argument('--backup-directory', type=Path)
    args = parser.parse_args()
    if args.apply and (not args.server_stopped or not args.backup_directory):
        parser.error('Apply requires --server-stopped and --backup-directory')
    pairs = [tuple(map(int, value.split(':'))) for value in args.pair]
    if any(len(pair) != 2 for pair in pairs):
        parser.error('Pairs must have source:target IDs')
    if len({p[0] for p in pairs}) != len(pairs):
        parser.error('Source profile repeated')
    with sqlite3.connect(args.database.resolve().as_uri() + '?mode=' + ('rw' if args.apply else 'ro'), uri=True) as original:
        print(json.dumps({'candidate_pairs_requiring_review': candidates(original)}))
        if not pairs and not args.repair_friend_links:
            return
        if args.apply:
            args.backup_directory.mkdir(mode=0o700, parents=True, exist_ok=True)
            backup = args.backup_directory / ('identity-' + datetime.datetime.now(datetime.timezone.utc).strftime('%Y%m%dT%H%M%S%fZ') + '.db')
            # Create privately and exclusively before SQLite opens the backup.
            os.close(os.open(backup, os.O_CREAT | os.O_EXCL | os.O_WRONLY, 0o600))
            with sqlite3.connect(backup) as destination:
                original.backup(destination)
                if destination.execute('PRAGMA integrity_check').fetchone()[0] != 'ok':
                    raise ValueError('Backup integrity check failed')
            backup.chmod(0o600)
            print(json.dumps({'backup': str(backup)}))
            db = original
        else:
            db = sqlite3.connect(':memory:')
            original.backup(db)
        db.execute('PRAGMA foreign_keys=ON')
        db.execute('BEGIN IMMEDIATE')
        try:
            reports = [unify(db, *pair) for pair in pairs]
            if args.repair_friend_links:
                reports.append(repair_friend_links(db))
            if db.execute('PRAGMA foreign_key_check').fetchall():
                raise ValueError('Foreign-key check failed')
            if db.execute('PRAGMA integrity_check').fetchone()[0] != 'ok':
                raise ValueError('Database integrity check failed')
            db.commit()
            print(json.dumps({'applied': args.apply, 'reports': reports}))
        except Exception:
            db.rollback()
            raise
        finally:
            if db is not original:
                db.close()


if __name__ == '__main__':
    main()
