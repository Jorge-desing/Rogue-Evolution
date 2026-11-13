// Console-free stdio adapter. Only the shared worker executes MCP tools.
const { spawn } = require('node:child_process');
const path = require('node:path');
const root = path.resolve(__dirname, '..');
const child = spawn(path.join(root, '.tools', 'BepInExMCP', '.venv', 'Scripts', 'python.exe'),
  [path.join(__dirname, 'bepinex_mcp_shared.py')], {
    cwd: root,
    windowsHide: true,
    stdio: ['pipe', 'pipe', 'pipe'],
    env: { ...process.env, BEPINEX_MCP_TOOL_FILTER: process.env.BEPINEX_MCP_TOOL_FILTER || 'includeAliases=false' },
  });
process.stdin.pipe(child.stdin);
child.stdout.pipe(process.stdout);
child.stderr.pipe(process.stderr);
child.stdin.on('error', () => {});
child.on('error', error => { process.stderr.write(`${error.message}\n`); process.exitCode = 1; });
child.on('exit', code => { process.exitCode = code ?? 1; });
for (const signal of ['SIGINT', 'SIGTERM']) process.on(signal, () => child.kill());
