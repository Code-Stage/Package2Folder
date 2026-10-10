#!/usr/bin/env python3
"""Publish validated product releases and merge stable history back to development."""

import argparse
from datetime import date, datetime, timezone
import json
import os
from pathlib import Path
import re
import subprocess
from urllib.parse import urlencode


VERSION = r'(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)'


def git(repo, *args, optional=False):
    result = subprocess.run(['git', '-C', str(repo), *args], text=True, capture_output=True)
    if result.returncode and not optional:
        raise ValueError(result.stderr.strip())
    return result.stdout.strip() if result.returncode == 0 else None


def ancestor(repo, older, newer):
    return git(repo, 'merge-base', '--is-ancestor', older, newer, optional=True) is not None


def top_version(repo, sha, config):
    text = git(repo, 'show', f"{sha}:{config['changelog']}", optional=True)
    if text is None:
        return None
    heading = re.search(r'^## .*$', text, re.MULTILINE)
    if heading:
        found = re.match(rf'^## \[?({VERSION})\]?(?:\s|$)', heading.group(0))
    else:
        found = re.search(rf'^({VERSION})\s*$', text, re.MULTILINE)
    return found.group(1) if found else None


def entry(repo, sha, config):
    text = git(repo, 'show', f"{sha}:{config['changelog']}", optional=True)
    if text is None:
        return None
    heading = re.search(r'^## .*$', text, re.MULTILINE)
    match = re.fullmatch(rf'## \[?({VERSION})\]? - (\d{{4}}-\d{{2}}-\d{{2}})',
                         heading.group(0)) if heading else None
    if not match:
        raise ValueError('Top changelog entry must be a dated x.y.z release')
    version, dated = match.groups()
    if date.fromisoformat(dated) > datetime.now(timezone.utc).date():
        raise ValueError('Changelog release date is in the future')
    version_file = config.get('version-file')
    if version_file:
        content = git(repo, 'show', f'{sha}:{version_file}')
        if version_file.endswith('.json'):
            actual = json.loads(content).get('version')
        elif version_file.endswith('.cs'):
            found = re.search(rf'\bVersion\s*=\s*"({VERSION})"', content)
            actual = found.group(1) if found else None
        else:
            actual = content.strip()
        if actual != version:
            raise ValueError(f'Changelog version {version} != version file {actual}')
    notes = re.split(rf'\n(?:## |{VERSION}\s*(?:\n|$))', text[heading.end():], maxsplit=1)[0].strip()
    if not notes or re.search(r'\b(TODO|FIXME|XXX)\b|^\s*-\s*$', notes, re.MULTILINE):
        raise ValueError('Release notes are empty or contain unfinished entries')
    return {'version': version, 'notes': notes, 'date': dated, 'sha': sha}


def provenance(repo, sha, config, version, api):
    release_branch = config.get('release-prefix', 'release/') + version
    ref = git(repo, 'rev-parse', '--verify', f'refs/remotes/origin/{release_branch}', optional=True)
    parents = git(repo, 'rev-list', '--parents', '-n', '1', sha).split()[1:]
    if len(parents) < 2:
        return False
    if ref and ref in parents[1:]:
        return True
    return any(pr.get('merged_at') and pr.get('merge_commit_sha') == sha
               and pr['base']['ref'] == config['stable-branch']
               and pr['head']['ref'] == release_branch
               and pr['head']['sha'] in parents[1:]
               for pr in api.request(f'commits/{sha}/pulls'))


def plan_release(repo, config, sha, event, api):
    if config.get('release-enabled', True) is False:
        return None
    version = top_version(repo, sha, config)
    if not version:
        if event == 'push':
            return None
        raise ValueError('Top changelog entry must contain a dated release version')
    existing = git(repo, 'rev-parse', '--verify', f'refs/tags/{version}^{{commit}}', optional=True)
    if existing and not ancestor(repo, existing, sha):
        raise ValueError(f'Existing tag {version} points outside this stable history')
    if event == 'workflow_dispatch' and existing:
        tagged = entry(repo, existing, config)
        if tagged is None or tagged['version'] != version:
            raise ValueError(f'Existing tag {version} has inconsistent version data')
        return tagged
    if event == 'workflow_dispatch':
        entry(repo, sha, config)
    # A manual repair searches history; a push can release only its exact head.
    history = [sha] if event == 'push' else git(repo, 'rev-list', '--first-parent', sha).splitlines()
    for candidate in history:
        if top_version(repo, candidate, config) != version:
            break
        parent = git(repo, 'rev-parse', '--verify', f'{candidate}^1', optional=True)
        previous = top_version(repo, parent, config) if parent else None
        # Installing version metadata into an old product must not publish a release.
        if previous is None:
            return None
        if previous == version:
            continue
        if not provenance(repo, candidate, config, version, api):
            return None
        candidate_entry = entry(repo, candidate, config)
        if tuple(map(int, version.split('.'))) <= tuple(map(int, previous.split('.'))):
            raise ValueError(f'Release {version} must be newer than stable version {previous}')
        tags = git(repo, 'tag', '--merged', candidate).splitlines()
        versions = [tuple(map(int, tag.split('.'))) for tag in tags
                    if re.fullmatch(VERSION, tag) and tag != version]
        if versions and tuple(map(int, version.split('.'))) <= max(versions):
            raise ValueError(f'Release {version} must be newer than existing versions')
        if existing and existing != candidate:
            raise ValueError(f'Existing tag {version} points to a different release commit')
        return candidate_entry
    return None


def publish(repo, config, plan, api):
    version, sha = plan['version'], plan['sha']
    tag = api.request(f'git/ref/tags/{version}', missing_ok=True)
    if tag:
        obj = tag['object']
        while obj['type'] == 'tag':
            obj = api.request(f"git/tags/{obj['sha']}")['object']
        if obj['sha'] != sha:
            raise ValueError(f'Existing remote tag {version} points to a different commit')
    else:
        api.request('git/refs', 'POST', {'ref': f'refs/tags/{version}', 'sha': sha})
    if not api.request(f'releases/tags/{version}', missing_ok=True):
        api.request('releases', 'POST', {'tag_name': version, 'target_commitish': sha,
                                       'name': version, 'body': plan['notes'],
                                       'draft': False, 'prerelease': False})
    stable, dev = config['stable-branch'], config['dev-branch']
    stable_sha = api.request(f'git/ref/heads/{stable}')['object']['sha']
    dev_sha = api.request(f'git/ref/heads/{dev}')['object']['sha']
    # Never move development backwards or rewrite its commits.
    if ancestor(repo, stable_sha, dev_sha):
        return
    if ancestor(repo, dev_sha, stable_sha):
        api.request(f'git/refs/heads/{dev}', 'PATCH', {'sha': stable_sha, 'force': False})
        return
    query = urlencode({'state': 'open', 'base': dev,
                       'head': f"{api.owner}:{stable}" if hasattr(api, 'owner') else stable})
    if not api.request(f'pulls?{query}'):
        api.request('pulls', 'POST', {'title': f'chore: merge release {version} into {dev}',
                                    'head': stable, 'base': dev,
                                    'body': f'Merge the published {version} release history into {dev}. Resolve any conflicts before merging.'})


def check_pr(config, event, repo):
    pr = event['pull_request']
    head, base = pr['head']['ref'], pr['base']['ref']
    stable, dev = config['stable-branch'], config['dev-branch']
    prefix = config.get('release-prefix', 'release/')
    if head.startswith('release/'):
        if not head.startswith(prefix) or not re.fullmatch(VERSION, head[len(prefix):]) or base != stable:
            raise ValueError(f'Release branches must target {stable} and use {prefix}<x.y.z>')
        if pr['head'].get('repo', {}).get('full_name') != pr['base'].get('repo', {}).get('full_name'):
            raise ValueError('Release branches must belong to the product repository')
        if config.get('release-enabled', True):
            release_entry = entry(repo, pr['head']['sha'], config)
            if release_entry is None or release_entry['version'] != head[len(prefix):]:
                raise ValueError('Release branch version must match its dated changelog and version file')
    elif base != dev:
        raise ValueError(f'Development changes must target {dev}; only release branches target {stable}')


class GitHub:
    def __init__(self, repository):
        self.repository = repository
        self.owner = repository.split('/')[0]

    def request(self, endpoint, method='GET', data=None, missing_ok=False):
        args = ['gh', 'api', f'repos/{self.repository}/{endpoint}', '--method', method]
        if data is not None:
            args += ['--input', '-']
        result = subprocess.run(args, input=json.dumps(data) if data is not None else None,
                                text=True, capture_output=True)
        if result.returncode:
            if missing_ok and 'HTTP 404' in result.stderr:
                return None
            raise ValueError(result.stderr.strip())
        return json.loads(result.stdout) if result.stdout else None


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--check-pr', action='store_true')
    parser.add_argument('--dry-run', action='store_true')
    args = parser.parse_args()
    config = json.loads(Path('.github/product-release.json').read_text(encoding='utf-8-sig'))
    if args.check_pr:
        check_pr(config, json.loads(Path(os.environ['GITHUB_EVENT_PATH']).read_text()), Path.cwd())
        print('Branch routing passed')
        return
    stable = config['stable-branch']
    if os.environ['GITHUB_REF'] != f'refs/heads/{stable}':
        print(f'Skipped: release publishing runs only on {stable}')
        return
    api = GitHub(os.environ['GITHUB_REPOSITORY'])
    plan = plan_release(Path.cwd(), config, os.environ['GITHUB_SHA'],
                        os.environ['GITHUB_EVENT_NAME'], api)
    if plan is None:
        print('Skipped: this commit is not a new product release')
        return
    print(json.dumps(plan, indent=2))
    if not args.dry_run:
        git(Path.cwd(), 'fetch', 'origin', '--prune', '--tags')
        publish(Path.cwd(), config, plan, api)


if __name__ == '__main__':
    main()
