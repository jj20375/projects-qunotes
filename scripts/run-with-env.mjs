import { readFile } from 'node:fs/promises';
import { spawn } from 'node:child_process';

const argumentsFromCli = process.argv.slice(2);
const separator = argumentsFromCli.indexOf('--');
const envFiles = separator > 0 ? argumentsFromCli.slice(0, separator) : [];
const [command, ...argumentsToRun] = separator > 0 ? argumentsFromCli.slice(separator + 1) : [];

if (!envFiles.length || !command) {
  throw new Error('用法：node scripts/run-with-env.mjs <env 檔案> [更多 env 檔案] -- <指令> [參數...]');
}

function parseEnv(source, fileName) {
  const values = {};
  for (const [index, rawLine] of source.split(/\r?\n/).entries()) {
    const line = rawLine.trim();
    if (!line || line.startsWith('#')) continue;

    const separator = line.indexOf('=');
    if (separator < 1) throw new Error(`${fileName}:${index + 1} 不是有效的 KEY=VALUE 設定。`);

    const key = line.slice(0, separator).trim();
    let value = line.slice(separator + 1).trim();
    if (!/^[A-Za-z_][A-Za-z0-9_]*$/.test(key)) throw new Error(`${fileName}:${index + 1} 的環境變數名稱無效。`);

    if ((value.startsWith('"') && value.endsWith('"')) || (value.startsWith("'") && value.endsWith("'"))) {
      value = value.slice(1, -1);
    }
    values[key] = value;
  }
  return values;
}

const environment = {};
for (const envFile of envFiles) {
  Object.assign(environment, parseEnv(await readFile(envFile, 'utf8'), envFile));
}
const executable = process.platform === 'win32' && command === 'npm' ? 'npm.cmd' : command;
const child = spawn(executable, argumentsToRun, {
  stdio: 'inherit',
  env: { ...process.env, ...environment },
});

child.on('error', (error) => {
  console.error(`無法執行 ${command}：${error.message}`);
  process.exit(1);
});

child.on('exit', (code, signal) => {
  if (signal) process.kill(process.pid, signal);
  process.exit(code ?? 1);
});
