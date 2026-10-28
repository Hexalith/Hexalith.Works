"""Keep release authority consistent with the locked GitHub plugin's executed hooks."""

import re
import subprocess
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


class ReleaseHookPermissionsTests(unittest.TestCase):
    def test_release_job_keeps_only_used_permission_grants(self):
        workflow = (ROOT / ".github/workflows/release.yml").read_text()
        job = re.search(r"(?ms)^  release:\n(.*?)(?=^  [\w-]+:|\Z)", workflow)
        self.assertIsNotNone(job)
        permissions = re.search(r"(?ms)^    permissions:\n((?:^      [^\n]+\n)+)", job[1])
        self.assertIsNotNone(permissions)
        self.assertEqual(dict(re.findall(r"^      ([\w-]+): (\w+)$", permissions[1], re.M)),
                         {"actions": "read", "contents": "write", "id-token": "write"})

    def test_locked_github_success_and_failure_hooks_need_no_issue_or_pull_request_authority(self):
        program = r'''
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import success from './node_modules/@semantic-release/github/lib/success.js';
import fail from './node_modules/@semantic-release/github/lib/fail.js';

const config = JSON.parse(readFileSync('.releaserc.json', 'utf8')).plugins
  .find(plugin => Array.isArray(plugin) && plugin[0] === '@semantic-release/github')[1];
const calls = [];
class ContentsOnlyOctokit {
  async request(route) {
    calls.push(route);
    if (route !== 'GET /repos/{owner}/{repo}') throw new Error(`Unexpected API request: ${route}`);
    return {data: {full_name: 'Hexalith/Hexalith.Works'}};
  }
  async graphql() { throw new Error('Unexpected GraphQL request'); }
}
const context = {
  env: {GITHUB_ACTION: 'true', GITHUB_TOKEN: 'synthetic-offline-token'},
  options: {repositoryUrl: 'https://github.com/Hexalith/Hexalith.Works.git'},
  commits: [{hash: 'a'.repeat(40), message: 'fix: synthetic release\n\nCloses #1'}],
  nextRelease: {version: '1.2.3', notes: 'Synthetic offline release'},
  releases: [], branch: {name: 'main'}, errors: [new Error('synthetic failure')],
  logger: {log() {}, warn() {}, error() {}},
};
const injection = {Octokit: ContentsOnlyOctokit};
await success(config, context, injection);
await fail(config, context, injection);
assert.deepEqual(calls, ['GET /repos/{owner}/{repo}']);

// Positive sensitivity controls: re-enabled issue/PR hooks must reach the denied API boundary.
await assert.rejects(success({...config, successCommentCondition: '<%= true %>'}, context, injection),
  /Unexpected GraphQL request/);
await assert.rejects(success({...config, failTitle: 'Failed release', failComment: 'Failure'}, context, injection),
  /Unexpected GraphQL request/);
await assert.rejects(fail({...config, failTitle: 'Failed release', failComment: 'Failure'}, context, injection),
  /Unexpected GraphQL request/);
console.log('Locked success/fail hooks passed without issue or pull-request authority.');
'''
        result = subprocess.run(["node", "--input-type=module", "-e", program], cwd=ROOT,
                                capture_output=True, text=True, timeout=30, check=False)
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn("hooks passed", result.stdout)
