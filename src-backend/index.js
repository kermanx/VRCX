const fs = require('fs');
const http = require('http');
const path = require('path');
const InteropApi = require('../src-electron/InteropApi');

const rootDir = path.join(__dirname, '..');
require(path.join(rootDir, 'build/Electron/VRCX-Electron.cjs'));

const interopApi = new InteropApi();

const version = getVersion();
console.log('Version:', version);

interopApi.getDotNetObject('ProgramElectron').PreInit(version, []);
interopApi.getDotNetObject('VRCXStorage').Load();
interopApi.getDotNetObject('ProgramElectron').Init();
interopApi.getDotNetObject('SQLiteLegacy').Init();
interopApi.getDotNetObject('AppApiElectron').Init();
interopApi.getDotNetObject('Discord').Init();
interopApi.getDotNetObject('WebApi').Init();
interopApi.getDotNetObject('LogWatcher').Init();

// ipcMain.handle('callDotNetMethod', (event, className, methodName, args) => {
//     return interopApi.callMethod(className, methodName, args);
// });

const server = http.createServer(async (req, res) => {
  if (req.method === 'GET') {
    const url = req.url === '/' ? '/index.html' : req.url;

    if (url === '/favicon.ico') {
      const file = fs.readFileSync(path.join(rootDir, 'VRCX.ico'));
      res.writeHead(200, { 'Content-Type': 'image/x-icon' });
      res.end(file);
      return;
    }

    const filePath = path.join(rootDir, 'build', 'html', url);
    if (path.relative(rootDir, filePath).startsWith('..')) {
      res.writeHead(404, { 'Content-Type': 'text/plain' });
      res.end('File not found');
      return;
    }
    if (fs.existsSync(filePath)) {
      const file = fs.readFileSync(filePath);
      const contentType = {
        '.js': 'application/javascript',
        '.css': 'text/css',
        '.html': 'text/html',
        '.json': 'application/json',
      }[path.extname(url)] || 'application/octet-stream';
      res.writeHead(200, { 'Content-Type': contentType });
      res.end(file);
    } else {
      res.writeHead(404, { 'Content-Type': 'text/plain' });
      res.end('File not found');
    }
  } else if (req.method === 'POST') {
    let body = '';
    req.on('data', chunk => {
      body += chunk.toString();
    });
    req.on('end', async () => {
      try {
        const { className, methodName, args } = JSON.parse(body);
        const result = await interopApi.callMethod(
          className,
          methodName,
          args.map(arg => {
            if (arg && arg.__is_map__) {
              const map = new Map();
              for (const key in arg) {
                if (key !== '__is_map__') {
                  map.set(key, arg[key]);
                }
              }
              return map;
            }
            return arg;
          }),
        );
        res.writeHead(200, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify({ status: 'success', result }));
      } catch (err) {
        console.error('Error:', err);
        res.writeHead(400, { 'Content-Type': 'text/plain' });
        res.end('Error processing request ' + err);
      }
    });
  } else {
    res.writeHead(405, { 'Content-Type': 'text/plain' });
    res.end('Method not allowed');
  }
});

const port = process.env.VRCX_PORT || 3333;
server.listen(port, () => {
  console.log(`Server is running on port ${port}`);
});

function getVersion() {
  try {
    var versionFile = fs
      .readFileSync(path.join(rootDir, 'Version'), 'utf8')
      .trim();

    // look for trailing git hash "-22bcd96" to indicate nightly build
    var version = versionFile.split('-');
    console.log('Version:', versionFile);
    if (version.length > 0 && version[version.length - 1].length == 7) {
      return `VRCX (Linux) Nightly ${versionFile}`;
    } else {
      return `VRCX (Linux) ${versionFile}`;
    }
  } catch (err) {
    console.error('Error reading Version:', err);
    return 'VRCX (Linux) Nightly Build';
  }
}
