import { spawnSync } from 'node:child_process';

/** Drops the database the run created. Best effort: a CI database service is thrown away anyway. */
export default function globalTeardown(): void {
  const db = process.env['E2E_DB'];
  if (!db || process.env['E2E_KEEP_DB']) return;

  const server = process.env['E2E_SQLCMD_SERVER'] ?? 'localhost\\SQLEXPRESS';
  const sql = `ALTER DATABASE [${db}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [${db}]`;
  const result = spawnSync('sqlcmd', ['-S', server, '-E', '-C', '-Q', sql], { encoding: 'utf8' });
  if (result.error || result.status !== 0) console.log(`(Could not drop ${db}; drop it by hand if it matters.)`);
}
