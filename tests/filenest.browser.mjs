import assert from 'node:assert/strict';
import { existsSync, mkdtempSync, mkdirSync, readFileSync, realpathSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { spawn, spawnSync } from 'node:child_process';
import { once } from 'node:events';
import { test } from 'node:test';

test('FileNest browser regression', { timeout: 120_000, skip: process.platform !== 'win32' }, async t => {
  const chromePaths = [
    'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe',
    'C:\\Program Files (x86)\\Google\\Chrome\\Application\\chrome.exe',
    process.env.PROGRAMFILES && path.join(process.env.PROGRAMFILES, 'Google\\Chrome\\Application\\chrome.exe')
  ].filter(Boolean);
  const chromePath = chromePaths.find(existsSync);
  if (!chromePath) {
    t.skip('Google Chrome is not installed');
    return;
  }

  const tempParent = realpathSync.native(tmpdir());
  const temp = realpathSync.native(mkdtempSync(path.join(tempParent, 'FileNest-browser-test-')));
  t.after(() => rmSync(temp, {
    recursive: true,
    force: true,
    maxRetries: 10,
    retryDelay: 200
  }));

  const publish = path.join(temp, 'publish');
  const files = path.join(temp, 'files');
  const data = path.join(temp, 'data');
  const projects = path.join(files, 'Projects');
  mkdirSync(projects, { recursive: true });
  mkdirSync(data, { recursive: true });
  writeFileSync(path.join(projects, 'Alpha report.txt'), 'alpha');
  writeFileSync(path.join(projects, 'Release Notes.txt'), 'release');

  const publishResult = spawnSync('dotnet', [
    'publish', 'FilePortal.csproj', '-c', 'Release', '-o', publish, '--nologo'
  ], { cwd: process.cwd(), encoding: 'utf8' });
  assert.equal(publishResult.status, 0, publishResult.stdout + publishResult.stderr);

  writeFileSync(path.join(publish, 'appsettings.json'), JSON.stringify({
    FilePortal: { DataDirectory: data },
    Roots: { Projects: projects },
    Email: { SmtpHost: '' }
  }));

  const password = 'browser-test-password';
  const provision = spawnSync('dotnet', ['FilePortal.dll', '--provision-admin', 'administrator'], {
    cwd: publish,
    encoding: 'utf8',
    input: password + '\n' + password + '\n',
    env: {
      ...process.env,
      ASPNETCORE_ENVIRONMENT: 'Production',
      DOTNET_ENVIRONMENT: 'Production'
    }
  });
  assert.equal(provision.status, 0, provision.stdout + provision.stderr);

  const app = spawn('dotnet', ['FilePortal.dll', '--urls', 'http://127.0.0.1:0'], {
    cwd: publish,
    env: {
      ...process.env,
      ASPNETCORE_ENVIRONMENT: 'Production',
      DOTNET_ENVIRONMENT: 'Production'
    },
    stdio: ['ignore', 'pipe', 'pipe']
  });
  const appExit = once(app, 'exit');
  let appOutput = '';
  app.stdout.on('data', chunk => { appOutput += chunk; });
  app.stderr.on('data', chunk => { appOutput += chunk; });

  let address;
  try {
    address = await new Promise((resolve, reject) => {
      const timeout = setTimeout(() => reject(new Error('Application startup timed out\n' + appOutput)), 20_000);
      const done = (error, result) => {
        clearTimeout(timeout);
        if (error) reject(error);
        else resolve(result);
      };
      app.once('error', done);
      app.once('exit', code => done(new Error('Application exited ' + code + '\n' + appOutput)));
      app.stdout.on('data', () => {
        const match = appOutput.match(/Now listening on: (http:\/\/127\.0\.0\.1:\d+)/);
        if (match) done(null, match[1]);
      });
    });
  } catch (error) {
    if (app.exitCode === null) app.kill();
    await appExit;
    throw error;
  }

  const profile = mkdtempSync(path.join(temp, 'chrome-profile-'));
  const chrome = spawn(chromePath, [
    '--headless=new', '--disable-gpu', '--no-first-run', '--disable-background-networking',
    '--remote-debugging-port=0', '--user-data-dir=' + profile, 'about:blank'
  ], { windowsHide: true, stdio: 'ignore' });

  let socket;
  try {
    const devToolsPortFile = path.join(profile, 'DevToolsActivePort');
    let port;
    for (let attempt = 0; attempt < 100 && !port; attempt++) {
      if (existsSync(devToolsPortFile))
        port = readFileSync(devToolsPortFile, 'utf8').split(/\r?\n/)[0];
      if (!port) await new Promise(resolve => setTimeout(resolve, 100));
    }
    assert.ok(port, 'Chrome did not start');

    const targets = await (await fetch('http://127.0.0.1:' + port + '/json/list')).json();
    const page = targets.find(target => target.type === 'page');
    assert.ok(page, 'Chrome page target not found');
    socket = new WebSocket(page.webSocketDebuggerUrl);
    await once(socket, 'open');

    const pending = new Map();
    let requestId = 0;
    const runtimeErrors = [];
    socket.addEventListener('message', event => {
      const message = JSON.parse(event.data);
      if (message.id && pending.has(message.id)) {
        const callback = pending.get(message.id);
        pending.delete(message.id);
        if (message.error) callback.reject(new Error(message.error.message));
        else callback.resolve(message.result);
      }
      if (message.method === 'Runtime.exceptionThrown')
        runtimeErrors.push(message.params.exceptionDetails.exception?.description || message.params.exceptionDetails.text);
      if (message.method === 'Page.javascriptDialogOpening')
        void send('Page.handleJavaScriptDialog', { accept: true });
    });

    function send(method, params = {}) {
      const id = ++requestId;
      socket.send(JSON.stringify({ id, method, params }));
      return new Promise((resolve, reject) => pending.set(id, { resolve, reject }));
    }

    async function evaluate(expression) {
      const result = await send('Runtime.evaluate', {
        expression,
        awaitPromise: true,
        returnByValue: true,
        userGesture: true
      });
      if (result.exceptionDetails)
        throw new Error(result.exceptionDetails.exception?.description || result.exceptionDetails.text);
      return result.result.value;
    }

    async function until(expression, label, attempts = 100) {
      for (let attempt = 0; attempt < attempts; attempt++) {
        const result = await evaluate(expression).catch(() => false);
        if (result) return;
        await new Promise(resolve => setTimeout(resolve, 100));
      }
      throw new Error(label);
    }

    async function navigate(route) {
      await send('Page.navigate', { url: address + route });
    }

    async function setViewport(width, height, mobile) {
      await send('Emulation.setTouchEmulationEnabled', { enabled: mobile, maxTouchPoints: mobile ? 5 : 1 });
      await send('Emulation.setDeviceMetricsOverride', {
        width,
        height,
        deviceScaleFactor: 1,
        mobile
      });
      assert.equal(await evaluate('innerWidth'), width);
      const noHorizontalOverflow = await evaluate('document.documentElement.scrollWidth <= innerWidth + 1');
      const overflow = noHorizontalOverflow ? '' : await evaluate(
        'JSON.stringify({innerWidth,clientWidth:document.documentElement.clientWidth,small:matchMedia("(max-width: 820px)").matches,' +
        'appColumns:getComputedStyle(document.querySelector("#mainApp")).gridTemplateColumns,' +
        'sidebar:getComputedStyle(document.querySelector("#driveSidebar")).display,elements:[...document.querySelectorAll("body *")].filter(element => {' +
        'const rect=element.getBoundingClientRect(); return rect.width && rect.right > innerWidth + 1' +
        '}).map(element => ({tag:element.tagName,className:element.className,id:element.id,right:element.getBoundingClientRect().right})).slice(0,8)})'
      );
      assert.equal(noHorizontalOverflow, true, overflow);
    }

    await send('Page.enable');
    await send('Runtime.enable');
    await navigate('/');
    await until('document.readyState === "complete" && Boolean(document.querySelector("#loginForm"))', 'Login page did not load');
    await evaluate(
      'document.querySelector("#username").value="administrator";' +
      'document.querySelector("#password").value=' + JSON.stringify(password) + ';' +
      'document.querySelector("#loginForm").requestSubmit(); true'
    );
    await until('document.querySelector("#dashboard") && !document.querySelector("#dashboard").hidden', 'Dashboard did not load');

    await setViewport(1440, 900, false);
    assert.equal(await evaluate('document.querySelectorAll("#quickRoots .fn-root").length'), 1);
    assert.equal(await evaluate('document.querySelector(".fn-nav").getBoundingClientRect().right <= innerWidth'), true);
    assert.deepEqual(await evaluate('[...document.querySelectorAll("#categorySection .fn-category")].map(element => element.textContent.trim().replace(/›$/, ""))'),
      ['Images', 'Videos', 'PDFs', 'Notes', 'Documents']);

    await setViewport(320, 844, true);
    assert.equal(await evaluate('document.querySelector(".fn-nav").getBoundingClientRect().right <= innerWidth'), true);
    assert.equal(await evaluate('document.querySelector(".fn-header").getBoundingClientRect().height > 0'), true);
    assert.equal(await evaluate('document.querySelector("#nestiCompanion") !== null'), true);
    assert.equal(await evaluate('document.querySelector("#nestiVoiceToggle").getAttribute("aria-pressed") === "true"'), true);
    assert.equal(await evaluate('(() => { const r=document.querySelector("#nestiCompanion").getBoundingClientRect(); return r.left >= 0 && r.top >= 0 && r.right <= innerWidth + 1 && r.bottom <= innerHeight + 1; })()'), true);
    await setViewport(360, 844, true);
    await setViewport(390, 844, true);
    await setViewport(430, 844, true);

    await evaluate('document.querySelector("#assistantLauncher").click(); true');
    await until('document.querySelector("#assistantPanel").open', 'Assistant dialog did not open');
    await evaluate('document.querySelector("#assistantPanel form button").click(); true');

    await navigate('/files.html');
    await until('location.pathname === "/files.html" && document.readyState === "complete"', 'File Manager did not load');
    await until('document.querySelectorAll("#driveList .drive-item").length === 1', 'File Manager folders did not load');
    await until('document.querySelector("#nestiCompanion") && document.querySelector("#assistantPanel")', 'Global Nesti assistant did not load');
    await setViewport(1440, 900, false);
    assert.equal(await evaluate('getComputedStyle(document.querySelector("#mainApp")).display'), 'grid');
    assert.equal(await evaluate('getComputedStyle(document.querySelector("#driveSidebar")).display'), 'flex');
    assert.equal(await evaluate('document.querySelector(".fn-explorer-title").closest(".main") !== null'), true);
    await evaluate('document.querySelector("#folderSearch").value="Release"; document.querySelector("#folderSearch").dispatchEvent(new Event("input")); true');
    await until('document.querySelectorAll("#items .item").length >= 1', 'File search did not return a result');
    assert.deepEqual(await evaluate('[...document.querySelectorAll("#items .item-name")].map(element => element.textContent)'), ['Release Notes.txt']);

    await setViewport(320, 844, true);
    assert.equal(await evaluate('getComputedStyle(document.querySelector("#mainApp")).display'), 'grid');
    assert.equal(await evaluate('getComputedStyle(document.querySelector("#driveSidebar")).display'), 'none');
    assert.equal(await evaluate('getComputedStyle(document.querySelector("#rootsToggle")).display !== "none"'), true);

    await navigate('/users.html');
    await until('document.querySelector("[data-edit-user=administrator]")', 'Settings user editor did not load');
    await evaluate('document.querySelector("[data-edit-user=administrator]").click(); true');
    await until('document.querySelector("#editModal").style.display === "flex"', 'User editor did not open');
    await evaluate(
      'document.querySelector("#editFirstName").value="Browser";' +
      'document.querySelector("#editLastName").value="Administrator";' +
      'document.querySelector("#editDialog button").click(); true'
    );
    await until('document.querySelector("#accountStatus").textContent === "Updated administrator."', 'User profile did not update');
    assert.match(await evaluate('document.querySelector(".user-summary strong").textContent'), /Browser Administrator/);
    await evaluate(
      'fetch("/api/users",{method:"POST",body:new URLSearchParams({username:"recovery-user",password:"InitialPassword123!",firstName:"Recovery",lastName:"User"})})' +
      '.then(response=>{if(!response.ok)throw new Error("Could not create recovery user");return load();}); true'
    );
    await until('document.querySelector("[data-password-user=recovery-user]")', 'Recovery user did not appear');
    await evaluate('document.querySelector("[data-password-user=recovery-user]").click(); true');
    await until('!document.querySelector("#temporaryPasswordSection").hidden', 'Temporary password controls did not appear');
    await evaluate('document.querySelector("#generateTemporaryPassword").click(); true');
    await until('document.querySelector("#temporaryPassword").value.length === 18', 'Temporary password was not generated');
    assert.equal(await evaluate('document.querySelector("#temporaryPasswordResult").hidden'), false);

    assert.deepEqual(runtimeErrors, []);
  } finally {
    socket?.close();
    if (!chrome.killed && chrome.exitCode === null) {
      const chromeExit = once(chrome, 'exit');
      chrome.kill();
      await chromeExit;
    }
    if (app.exitCode === null) {
      app.kill();
      await appExit;
    }
  }
});
