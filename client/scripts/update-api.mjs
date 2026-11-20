// Refreshes client/openapi.json from the running code of the API (through its contract test),
// then regenerates the TypeScript client in src/app/api from it.
import { spawnSync } from 'node:child_process';

const run = (cmd, args, env = {}) => {
  const r = spawnSync(cmd, args, { stdio: 'inherit', shell: true, env: { ...process.env, ...env } });
  if (r.status !== 0) process.exit(r.status ?? 1);
};

run('dotnet', ['test', '../TaskManager.Tests', '--filter', 'OpenApiContract'], { UPDATE_OPENAPI: '1' });
run('npx', ['ng-openapi-gen']);
