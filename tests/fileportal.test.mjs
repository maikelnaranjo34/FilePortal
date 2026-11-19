import assert from 'node:assert/strict';
import { spawn, spawnSync } from 'node:child_process';
import { pbkdf2Sync, randomBytes } from 'node:crypto';
import { once } from 'node:events';
import { chmodSync, existsSync, mkdirSync, mkdtempSync, readFileSync, realpathSync, rmSync, symlinkSync, truncateSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { test } from 'node:test';
import { Script } from 'node:vm';

test('FilePortal release integration', { timeout: 90_000 }, async t => {
  const tempParent = realpathSync.native(tmpdir());
  const temp = realpathSync.native(mkdtempSync(path.join(tempParent, 'FilePortal-test-')));
  t.after(() => {
    assert.equal(path.dirname(temp), tempParent);
    assert.match(path.basename(temp), /^FilePortal-test-/);
    rmSync(temp, { recursive: true, force: true, maxRetries: 3 });
  });

  const publish = path.join(temp, 'publish');
  const files = path.join(temp, 'files');
  const allowed = path.join(files, 'Assigned');
  const unassigned = path.join(files, 'Other');
  const data = path.join(files, 'ApplicationData');
  const outside = path.join(temp, 'Outside');
  for (const directory of [allowed, unassigned, data, outside]) mkdirSync(directory, { recursive: true });

  const text = path.join(allowed, 'note.txt');
  const video = path.join(allowed, 'video.mp4');
  const photo = path.join(allowed, 'photo.png');
  const hidden = path.join(unassigned, 'other.txt');
  writeFileSync(text, 'assigned');
  writeFileSync(video, '0123456789');
  writeFileSync(hidden, 'other');
  writeFileSync(photo, Buffer.from(
    'iVBORw0KGgoAAAANSUhEUgAAAAQAAAACCAIAAADwyuo0AAAAEElEQVR4nGNQmPAfjhiQOQCoOg151fcjLwAAAABJRU5ErkJggg==', 'base64'));
  const installed = path.join(files, 'Program Files', 'Fixture App');
  mkdirSync(installed, { recursive: true });
  const bundledImage = path.join(installed, 'AppInterface.png');
  writeFileSync(bundledImage, readFileSync(photo));
  const throughLink = path.join(allowed, 'Linked');
  symlinkSync(unassigned, throughLink, process.platform === 'win32' ? 'junction' : 'dir');

  const run = (args, options) => spawnSync('dotnet', args, { encoding: 'utf8', ...options });
  const build = run(['publish', 'FilePortal.csproj', '-c', 'Release', '-o', publish, '--nologo']);
  assert.equal(build.status, 0, build.stdout + build.stderr);
  const webConfig = readFileSync(path.join(publish, 'web.config'), 'utf8');
  assert.equal(webConfig.match(/arguments="([^"]*)"/)?.[1], '.\\FilePortal.dll');
  for (const name of ['index.html', 'gallery.html', 'files.html', 'login.html', 'users.html', 'share.html']) {
    const html = readFileSync(path.join(publish, 'wwwroot', name), 'utf8');
    for (const script of html.matchAll(/<script\b[^>]*>([\s\S]*?)<\/script>/gi))
      new Script(script[1], { filename: name });
  }
  for (const name of [
    'app.js', 'theme.js', 'preview.js', 'range-selection.js', 'filenest/route.js',
    'filenest/shell.js', 'filenest/dashboard.js', 'filenest/assistant.js'
  ])
    new Script(readFileSync(path.join(publish, 'wwwroot', name), 'utf8'), { filename: name });

  writeFileSync(path.join(publish, 'appsettings.json'), JSON.stringify({
    FilePortal: { DataDirectory: data },
    Roots: { Fixture: files },
    Email: { SmtpHost: '' }
  }));
  const ffmpegCommand = process.platform === 'win32' ? 'where.exe' : 'which';
  const ffmpeg = spawnSync(ffmpegCommand, ['ffmpeg'], { encoding: 'utf8' }).stdout
    ?.split(/\r?\n/).find(line => path.isAbsolute(line.trim()))?.trim();
  const env = Object.fromEntries(Object.entries(process.env).filter(([name]) =>
    !/^(FilePortal|Roots|Email|Auth|VideoThumbnails)__/i.test(name)));
  Object.assign(env, { ASPNETCORE_ENVIRONMENT: 'Production', DOTNET_ENVIRONMENT: 'Production' });
  if (ffmpeg) env.VideoThumbnails__FfmpegPath = ffmpeg;

  const password = 'integration-test-password';
  const adminArgs = ['FilePortal.dll', '--provision-admin', 'administrator'];
  const bootstrapped = run(adminArgs, { cwd: publish, env, input: password + '\n' + password + '\n' });
  assert.equal(bootstrapped.status, 0, bootstrapped.stdout + bootstrapped.stderr);
  assert.equal(run(adminArgs, { cwd: publish, env, input: password + '\n' + password + '\n' }).status, 1);

  const userFile = path.join(data, 'users.json');
  const fromPreviousVersion = JSON.parse(readFileSync(userFile, 'utf8'));
  const legacySalt = randomBytes(16);
  fromPreviousVersion.push({
    Username: 'legacy',
    IsAdmin: false,
    Salt: legacySalt.toString('base64'),
    Hash: pbkdf2Sync(password, legacySalt, 100000, 32, 'sha256').toString('base64'),
    AllowedFolders: [allowed]
  });
  writeFileSync(userFile, JSON.stringify(fromPreviousVersion));

  const child = spawn('dotnet', ['FilePortal.dll', '--urls', 'http://127.0.0.1:0'], {
    cwd: publish, env, stdio: ['ignore', 'pipe', 'pipe']
  });
  const childExit = once(child, 'exit');
  let output = '';
  child.stderr.on('data', chunk => { output += chunk; });
  try {
    const address = await new Promise((resolve, reject) => {
      const timeout = setTimeout(() => reject(new Error('Application startup timed out\n' + output)), 20_000);
      const done = (error, result) => {
        clearTimeout(timeout);
        if (error) reject(error);
        else resolve(result);
      };
      child.once('error', done);
      child.once('exit', code => done(new Error('Application exited ' + code + '\n' + output)));
      child.stdout.on('data', chunk => {
        output += chunk;
        const match = output.match(/Now listening on: (http:\/\/127\.0\.0\.1:\d+)/);
        if (match) done(null, match[1]);
      });
    });

    const at = (route, value, key = 'path') => route + '?' + new URLSearchParams({ [key]: value });
    const json = body => ({ method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify(body) });
    const form = fields => ({ method: 'POST', body: new URLSearchParams(fields) });
    async function request(route, status, cookie, options = {}) {
      const response = await fetch(address + route, {
        ...options, headers: { ...options.headers, ...(cookie ? { cookie } : {}) },
        redirect: 'manual', signal: AbortSignal.timeout(10_000)
      });
      assert.equal(response.status, status, route + (response.status !== status ? '\n' + await response.text() + '\n' + output : ''));
      return response;
    }
    async function login(username, credentials = password) {
      const response = await request('/api/auth/login', 200, null, form({ username, password: credentials }));
      return response.headers.get('set-cookie').split(';')[0];
    }

    const landing = await request('/', 200);
    assert.match(landing.headers.get('cache-control'), /no-cache/);
    const landingHtml = await landing.text();
    assert.match(landingHtml, /id="dashboard"/);
    assert.match(landingHtml, /id="assistantPanel"/);
    assert.match(landingHtml, /src="\/filenest\/route\.js"/);
    const gallery = await request('/gallery.html', 200);
    const galleryHtml = await gallery.text();
    for (const view of ['photos', 'videos', 'albums', 'favorites', 'search'])
      assert.match(galleryHtml, new RegExp('href="#' + view + '"'));
    assert.match(galleryHtml, /src="\/app\.js(?:\?[^"]+)?"/);
    for (const asset of ['shell.js', 'dashboard.js', 'assistant.js', 'site.css', 'workspace.css', 'mascot.png'])
      await request('/filenest/' + asset, 200);
    const galleryScript = await request('/app.js', 200);
    assert.match(galleryScript.headers.get('cache-control'), /must-revalidate/);
    assert.match(galleryScript.headers.get('content-type'), /javascript/);
    await request('/theme.js', 200);
    assert.match(readFileSync(path.join(publish, 'wwwroot', 'theme.js'), 'utf8'),
      /addEventListener\("contextmenu"/);
    await request('/preview.js', 200);
    await request('/range-selection.js', 200);
    await request('/login.html', 200);
    const fileManager = await request('/files.html', 200);
    assert.match(fileManager.headers.get('cache-control'), /no-cache/);
    assert.match(await fileManager.text(), /archiveDownload/);
    assert.equal((await request('/portal.html', 302)).headers.get('location'), '/');
    assert.equal((await request('/FileManager', 302)).headers.get('location'), '/files.html');
    await request('/api/unknown', 404);
    await request('/api/users/init', 404);
    await request('/api/auth/login', 401, null, form({ username: 'unknown', password }));
    await request('/api/files/roots', 401);
    await request('/api/photos/timeline', 401);
    await request('/api/albums', 401);
    await request('/api/search', 401);
    const admin = await login('administrator');
    const legacy = await login('legacy');
    const assigned = await request('/api/files/roots', 200, legacy);
    assert.deepEqual(await assigned.json(), [{ name: 'Assigned', path: allowed }]);

    const listed = await request(at('/api/files/folder', allowed), 200, legacy);
    assert.deepEqual((await listed.json()).map(item => item.name).sort(), ['note.txt', 'photo.png', 'video.mp4']);
    await request(at('/api/files/browse', allowed), 403, legacy);
    await request(at('/api/files/browse', files), 200, admin);
    await request(at('/api/files/folder', unassigned), 403, legacy);
    await request(at('/api/files/open', hidden), 403, legacy);
    await request(at('/api/files/file', hidden), 403, legacy);
    await request(at('/api/files/thumbnail', hidden), 403, legacy);
    await request(at('/api/files/category', unassigned) + '&category=documents', 403, legacy);
    await request('/api/files/download-zip', 403, legacy, json({ paths: [text, hidden] }));
    await request(at('/api/files/upload', unassigned, 'targetPath'), 403, legacy, form({}));
    await request(at('/api/files/delete', hidden), 403, legacy, { method: 'DELETE' });
    await request(at('/api/files/folder', throughLink), 403, legacy);
    await request(at('/api/files/delete', throughLink), 403, legacy, { method: 'DELETE' });
    assert.equal(existsSync(hidden), true);
    await request(at('/api/files/folder', outside), 403, admin);
    await request(at('/api/files/file', userFile), 403, admin);
    await request(at('/api/files/delete', files), 403, admin, { method: 'DELETE' });

    const contents = await request(at('/api/files/open', text), 200, legacy);
    assert.equal(await contents.text(), 'assigned');
    const partial = await request(at('/api/files/file', video), 206, legacy, { headers: { range: 'bytes=2-4' } });
    assert.equal(partial.headers.get('content-range'), 'bytes 2-4/10');
    assert.equal(await partial.text(), '234');
    const thumbnail = await request(at('/api/files/thumbnail', photo), 200, legacy);
    assert.equal(thumbnail.headers.get('content-type'), 'image/jpeg');
    assert.equal(Buffer.from(await thumbnail.arrayBuffer()).subarray(0, 2).toString('hex'), 'ffd8');
    const category = await request(at('/api/files/category', allowed) + '&category=documents', 200, legacy);
    assert.deepEqual((await category.json()).map(item => item.name), ['note.txt']);

    await request('/api/photos/scan', 200, admin, { method: 'POST' });
    const bundledSearch = await request('/api/search?q=AppInterface.png', 200, admin);
    assert.equal((await bundledSearch.json()).total, 0);
    assert.equal(existsSync(bundledImage), true);
    const timeline = await request('/api/photos/timeline', 200, legacy);
    const photoItems = (await timeline.json()).sections.flatMap(section => section.photos);
    const videoTimeline = await request('/api/photos/timeline?videosOnly=true&take=1', 200, legacy);
    const videoPage = await videoTimeline.json();
    assert.equal(videoPage.total, 1);
    assert.deepEqual(videoPage.sections.flatMap(section => section.photos.map(item => item.fileName)), ['video.mp4']);
    assert.deepEqual(photoItems.map(item => item.fileName).sort(), ['photo.png', 'video.mp4']);
    const videoId = videoPage.sections[0].photos[0].id;
    const videoThumbnail = '/api/photos/' + videoId + '/thumbnail';
    assert.equal((await request(videoThumbnail, 200, legacy)).headers.get('content-type'), 'image/svg+xml');
    await request(videoThumbnail, 401);
    await t.test('video thumbnails use a frame near two seconds and handle short videos',
      { skip: !ffmpeg && 'FFmpeg is unavailable' }, async () => {
        const generate = args => {
          const result = spawnSync(ffmpeg, ['-y', '-v', 'error', ...args, '-c:v', 'mpeg4', video], { encoding: 'utf8' });
          assert.equal(result.status, 0, result.stderr);
        };
        async function jpeg(route, cookie) {
          const response = await request(route, 200, cookie);
          assert.equal(response.headers.get('content-type'), 'image/jpeg');
          return Buffer.from(await response.arrayBuffer());
        }
        generate([
          '-f', 'lavfi', '-i', 'color=red:s=320x180:d=1',
          '-f', 'lavfi', '-i', 'color=blue:s=320x180:d=2',
          '-filter_complex', '[0:v][1:v]concat=n=2:v=1:a=0'
        ]);
        const preview = await jpeg(videoThumbnail, legacy);
        assert.deepEqual(await jpeg(at('/api/files/thumbnail', video), legacy), preview);
        const shared = await request('/api/sharing', 200, legacy, json({ type: 'photo', targetId: videoId }));
        const sharedVideo = '/api/sharing/public/' + (await shared.json()).token + '/photo/' + videoId + '/thumbnail';
        assert.deepEqual(await jpeg(sharedVideo), preview);
        const pixel = spawnSync(ffmpeg, [
          '-v', 'error', '-f', 'mjpeg', '-i', 'pipe:0', '-vf', 'scale=1:1',
          '-pix_fmt', 'rgb24', '-f', 'rawvideo', 'pipe:1'
        ], { input: preview });
        assert.equal(pixel.status, 0, pixel.stderr.toString());
        assert.ok(pixel.stdout[2] > 200 && pixel.stdout[0] < 40);
        generate(['-f', 'lavfi', '-i', 'color=red:s=320x180:d=0.25']);
        assert.notDeepEqual(await jpeg(videoThumbnail, legacy), preview);
      });
    const photoId = photoItems.find(item => item.fileName === 'photo.png').id;
    await request('/api/photos/' + photoId + '/thumbnail', 200, legacy);
    await request('/api/photos/' + photoId + '/favorite', 200, legacy, json({ favorite: true }));
    const favorites = await request('/api/photos/timeline?favorites=true', 200, legacy);
    assert.deepEqual((await favorites.json()).sections.flatMap(section => section.photos).map(item => item.id), [photoId]);
    const album = await request('/api/albums', 200, legacy, json({ name: 'Fixture album', photoIds: [photoId] }));
    const albumId = (await album.json()).id;
    const albums = await request('/api/albums/' + albumId, 200, legacy);
    assert.deepEqual((await albums.json()).photos.map(item => item.id), [photoId]);
    const search = await request('/api/search?q=photo.png', 200, legacy);
    assert.deepEqual((await search.json()).results.map(item => item.id), [photoId]);
    const addedShare = await request('/api/sharing', 200, legacy, json({ type: 'photo', targetId: photoId }));
    const token = (await addedShare.json()).token;
    await request('/api/sharing/public/' + token + '/photo/' + photoId + '/file', 200);
    const photoZip = await request('/api/photos/batch/download-form', 200, legacy, form({ ids: photoId }));
    assert.equal(photoZip.headers.get('content-type'), 'application/zip');
    assert.ok(Buffer.from(await photoZip.arrayBuffer()).includes('photo.png'));

    const externalPhoto = path.join(allowed, 'Removed in Explorer.png');
    writeFileSync(externalPhoto, readFileSync(photo));
    await request('/api/photos/scan', 200, admin, { method: 'POST' });
    const indexedExternalPhoto = await request('/api/search?q=Removed%20in%20Explorer', 200, legacy);
    assert.equal((await indexedExternalPhoto.json()).total, 1);
    rmSync(externalPhoto);
    const updatedSearch = await request('/api/search?q=Removed%20in%20Explorer', 200, legacy);
    assert.equal((await updatedSearch.json()).total, 0);
    const updatedTimeline = await request('/api/photos/timeline', 200, legacy);
    assert.equal((await updatedTimeline.json()).total, photoItems.length);

    const upload = new FormData();
    upload.append('files', new Blob(['new file']), 'Nested/new.txt');
    await request(at('/api/files/upload', allowed, 'targetPath'), 200, legacy, { method: 'POST', body: upload });
    const newFile = path.join(allowed, 'Nested', 'new.txt');
    assert.equal(readFileSync(newFile, 'utf8'), 'new file');
    const archive = await request('/api/files/download-zip-form', 200, legacy, form({ paths: allowed }));
    assert.equal(archive.headers.get('content-type'), 'application/zip');
    const zip = Buffer.from(await archive.arrayBuffer());
    assert.equal(zip.subarray(0, 4).toString('hex'), '504b0304');
    assert.ok(zip.includes('Assigned/Nested/new.txt'));
    assert.ok(!zip.includes('other.txt'));
    const duplicateUpload = new FormData();
    duplicateUpload.append('files', new Blob(['first']), 'duplicate.txt');
    duplicateUpload.append('files', new Blob(['second']), 'duplicate.txt');
    await request(at('/api/files/upload', allowed, 'targetPath'), 409, legacy, { method: 'POST', body: duplicateUpload });
    assert.equal(existsSync(path.join(allowed, 'duplicate.txt')), false);
    await request(at('/api/files/delete', newFile), 200, legacy, { method: 'DELETE' });
    assert.equal(existsSync(newFile), false);

    const firstBulk = path.join(allowed, 'Bulk first.txt');
    const secondBulk = path.join(allowed, 'Bulk second.txt');
    const missingBulk = path.join(allowed, 'Missing file.txt');
    writeFileSync(firstBulk, 'first');
    writeFileSync(secondBulk, 'second');
    await request('/api/files/batch/delete', 403, legacy, json({ paths: [firstBulk, hidden] }));
    await request('/api/files/batch/delete', 403, legacy, json({ paths: [allowed, secondBulk] }));
    assert.equal(existsSync(firstBulk), true);
    assert.equal(existsSync(secondBulk), true);
    const deletedBatch = await request('/api/files/batch/delete', 200, legacy, json({
      paths: [firstBulk, secondBulk, firstBulk, missingBulk]
    }));
    const deletedFiles = await deletedBatch.json();
    assert.deepEqual(deletedFiles.deleted.sort(), [firstBulk, secondBulk].sort());
    assert.equal(deletedFiles.failures[0].path, missingBulk);
    assert.equal(existsSync(firstBulk), false);
    assert.equal(existsSync(secondBulk), false);
    assert.equal(existsSync(hidden), true);

    const streamFile = path.join(allowed, 'Open preview.mp4');
    writeFileSync(streamFile, '');
    truncateSync(streamFile, 16 * 1024 * 1024);
    const activePreview = await request(at('/api/files/file', streamFile), 200, legacy);
    try {
      const result = await request('/api/files/batch/delete', 200, legacy, json({ paths: [streamFile] }));
      assert.deepEqual((await result.json()).deleted, [streamFile]);
    } finally {
      await activePreview.body.cancel();
    }
    assert.equal(existsSync(streamFile), false);

    if (process.platform === 'win32') {
      const protectedFile = path.join(allowed, 'Read-only.txt');
      writeFileSync(protectedFile, 'fixture');
      chmodSync(protectedFile, 0o444);
      try {
        const result = await request('/api/files/batch/delete', 200, legacy, json({ paths: [protectedFile] }));
        assert.deepEqual((await result.json()).failures, [{ path: protectedFile, error: 'The file is read-only.' }]);
        assert.equal(existsSync(protectedFile), true);
      } finally {
        chmodSync(protectedFile, 0o666);
      }

      const hold = [
        '$file = [IO.File]::OpenRead($env:FilePortalFixtureLockPath)',
        'try { [Console]::WriteLine("ready"); $null = [Console]::ReadLine() } finally { $file.Dispose() }'
      ].join('\n');
      const locker = spawn('powershell.exe', ['-NoLogo', '-NoProfile', '-NonInteractive', '-EncodedCommand',
        Buffer.from(hold, 'utf16le').toString('base64')], {
        env: { ...process.env, FilePortalFixtureLockPath: protectedFile },
        windowsHide: true, stdio: ['pipe', 'pipe', 'pipe']
      });
      const released = once(locker, 'exit');
      try {
        const [ready] = await once(locker.stdout, 'data');
        assert.match(ready.toString(), /ready/);
        const result = await request('/api/files/batch/delete', 200, legacy, json({ paths: [protectedFile] }));
        assert.deepEqual((await result.json()).failures, [{
          path: protectedFile, error: 'The file is open in another program. Close it and try again.'
        }]);
      } finally {
        locker.stdin.end('\n');
        await released;
      }
      await request(at('/api/files/delete', protectedFile), 200, legacy, { method: 'DELETE' });
    }

    async function add(username) {
      await request('/api/users', 201, admin, form({ username, password, firstName: 'Fixture', lastName: 'User' }));
    }
    await Promise.all(['viewer', 'second-user', 'third-user', 'fourth-user'].map(add));
    const users = await request('/api/users', 200, admin);
    assert.equal((await users.json()).length, 6);
    await request('/api/users/viewer/folders', 200, admin, json([allowed]));
    await request('/api/users/viewer/folders', 400, admin, json([outside]));
    const viewer = await login('viewer');
    await request(at('/api/files/open', text), 200, viewer);
    await request('/api/users/viewer/folders', 200, admin, json([]));
    await request(at('/api/files/open', text), 403, viewer);
    await request('/api/users/viewer/promote', 200, admin, { method: 'POST' });
    await request('/api/auth/me', 401, viewer);
    const promoted = await login('viewer');
    await request('/api/users', 200, promoted);
    await request('/api/users/viewer/demote', 200, admin, { method: 'POST' });
    await request('/api/users', 401, promoted);

    const newPassword = 'new-integration-password';
    await request('/api/users/legacy/password', 400, legacy, form({ newPassword, currentPassword: 'incorrect' }));
    await request('/api/users/legacy/password', 200, legacy, form({ newPassword, currentPassword: password }));
    await request('/api/auth/me', 401, legacy);
    const changed = await login('legacy', newPassword);
    await request('/api/users/legacy/password', 200, admin, form({ newPassword: password }));
    await request('/api/auth/me', 401, changed);
    const reset = await login('legacy');
    await request('/api/users/legacy/folders', 200, admin, json([]));
    await request('/api/photos/' + photoId + '/file', 404, reset);
    await request('/api/sharing/public/' + token + '/photo/' + photoId + '/file', 404);
    await request('/api/photos/batch/delete', 403, reset, json({ ids: [photoId] }));
    await request('/api/photos/batch/delete', 404, admin, json({ ids: [photoId, 'missing-id'] }));
    assert.equal(existsSync(photo), true);
    const galleryDeletion = await request('/api/photos/batch/delete', 200, admin, json({
      ids: photoItems.map(item => item.id)
    }));
    const deletedPhotos = await galleryDeletion.json();
    assert.equal(deletedPhotos.deleted, 2);
    assert.deepEqual(deletedPhotos.deletedIds.sort(), photoItems.map(item => item.id).sort());
    assert.equal(existsSync(photo), false);
    assert.equal(existsSync(video), false);
    await request('/api/users/legacy', 204, admin, { method: 'DELETE' });
    await request('/api/auth/me', 401, reset);

    writeFileSync(userFile, 'invalid json');
    await request('/api/auth/login', 500, null, form({ username: 'unknown', password }));
    assert.equal(readFileSync(userFile, 'utf8'), 'invalid json');
  } finally {
    if (child.exitCode === null) child.kill();
    await childExit;
  }
});
