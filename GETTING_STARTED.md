# FilePortal - Getting Started Guide

## Quick Start (5 minutes)

### 1. Build & Run
```bash
cd d:\Dev\Fileportal
dotnet build
dotnet run
```

Server starts on `http://localhost:5251`

### 2. First-Time Setup
Visit `http://localhost:5251/users.html`

You'll see this form:
```
Create Initial Admin
├─ Admin username: [_____________]
├─ Password: [_____________]
└─ Create Admin [Button]
```

Enter:
- Username: `admin`
- Password: `YourSecurePassword123`

Click "Create Admin" → Redirected to main app

### 3. Login to File Browser
At `http://localhost:5251/`:
```
FilePortal Login
├─ Username: admin
├─ Password: [YourSecurePassword123]
└─ Login [Button]
```

Click Login → File browser appears with your drives

## Features Overview

### File Browser
- Navigate local drives (E:, F:, H:)
- Browse network shares (\\192.168.5.1\...)
- View Samsung phone backups
- Folder icons with purple (#707be8) thumbnails
- Image previews for photos

### User Management
Click "Manage Users" (top right) to:
- **View** all users and their admin status
- **Add** new users (with optional admin privilege)
- **Promote** users to admin
- **Demote** admins to regular users
- **Remove** users from system

### Safety Features
- ✅ Last admin is protected (cannot be removed or demoted)
- ✅ You cannot remove yourself
- ✅ Passwords are securely hashed (PBKDF2)
- ✅ Sessions expire after 7 days
- ✅ C: drive is excluded from access

## Configuration

### Using Environment Variables (Recommended)
```bash
# Set before running
$env:Auth__Username = "myusername"
$env:Auth__Password = "mypassword"

dotnet run
```

These override the hardcoded defaults on first login.

### Using appsettings.json (Fallback)
Edit `appsettings.json`:
```json
{
  "Roots": { ... },
  "Auth": {
    "Username": "myusername",
    "Password": "mypassword"
  }
}
```

### Hardcoded Defaults (Last Resort)
If no env vars or config, defaults are:
- Username: `maikel`
- Password: `Cafeta0823$`

**Note**: After first user creation, only users in the database can login.

## Common Tasks

### Reset All Users
```bash
# Stop the application
# Delete the user database
rm data/users.json

# Restart application
dotnet run

# Visit /users.html to create new initial admin
```

### Create User (As Admin)
1. Go to "Manage Users"
2. Fill "Add User" section:
   - Username: `john`
   - Password: `secure123`
   - ☐ Make admin (leave unchecked for regular user)
3. Click "Add"

### Promote User to Admin
1. Go to "Manage Users"
2. Find user in list
3. Click "Promote" button
4. User now has admin permissions

### Remove User
1. Go to "Manage Users"
2. Find user in list
3. Click "Remove" button
4. Confirm in dialog
5. User is deleted

## Troubleshooting

### "Cannot create initial admin" error
**Cause**: Users already exist in data/users.json
**Fix**: Delete data/users.json and restart

### "Users already exist" on /api/users/init
**Cause**: First-run protection; the system is already initialized
**Fix**: Go to "Manage Users" to add more users

### "Cannot remove the last admin" error
**Cause**: System requires at least one admin user
**Fix**: Create another admin user before removing the current one

### Password changed but still can't login
**Cause**: Password is saved to database, not reflected until page refresh
**Fix**: Refresh browser and try again

### "Unauthorized" error on file browser
**Cause**: Session cookie expired (7 days)
**Fix**: Go back to login page and login again

### C: drive not showing
**Intentional**: C: drive is excluded from access for security
**Workaround**: Access needed files through a network share instead

### Cannot see "Manage Users" button
**Cause**: User account is not an admin
**Fix**: Ask an admin to promote your account

## Security Best Practices

### Passwords
- Use 12+ characters
- Include uppercase, lowercase, numbers, symbols
- Don't reuse passwords from other services
- Change regularly (monthly recommended)

### Admin Access
- Only create admin accounts when necessary
- Prefer regular user accounts for routine use
- Audit user list weekly
- Remove unused accounts promptly

### Sessions
- Logout when done (`Logout` button, top right)
- Don't leave browser unattended
- Use private/incognito mode on shared computers

### Files
- Don't store sensitive data in accessible shares
- Respect other users' folders
- Virus scan before uploading executables
- Backup important files regularly

## Documentation Files

| File | Purpose |
|------|---------|
| `README.md` | Build and run instructions |
| `SECRETS.md` | Environment variable setup |
| `SECURITY_IMPLEMENTATION.md` | Architecture and security details |
| `TESTING_CHECKLIST.md` | 10 comprehensive test scenarios |
| `IMPLEMENTATION_COMPLETE.md` | Full session summary |

## API Endpoints (Advanced)

### Authentication
```
POST /api/auth/login
  Body: { username: "user", password: "pass" }
  Response: 200 OK (sets cookie)

POST /api/auth/logout
  Response: 200 OK (clears cookie)

GET /api/auth/me
  Response: { user: "username", isAdmin: true }
```

### User Management (Admin-only except /init)
```
GET /api/users/init
  Response: { isEmpty: true/false }

POST /api/users/init
  Body: { username: "admin", password: "pass" }
  Response: 201 Created (creates initial admin)

GET /api/users
  Response: [{ username: "user1", isAdmin: true }, ...]

POST /api/users
  Body: { username: "user2", password: "pass", isAdmin: false }
  Response: 201 Created

DELETE /api/users/user1
  Response: 204 No Content

POST /api/users/user1/promote
  Response: 200 OK

POST /api/users/user1/demote
  Response: 200 OK
```

### File Browser
```
GET /api/files/roots
  Response: [{ name: "Disk E:", path: "E:" }, ...]

GET /api/files/folder?path=E:\Documents
  Response: [{ name: "file.txt", isDirectory: false, ... }, ...]

GET /api/files/thumbnail?path=E:\image.jpg
  Response: Image data (SVG for folders, JPEG for images)

GET /api/files/open?path=E:\file.txt
  Response: File content (for download or preview)
```

## Support

### Getting Help
1. Check relevant markdown file in repo
2. Review test scenarios in `TESTING_CHECKLIST.md`
3. Check `SECURITY_IMPLEMENTATION.md` for architecture questions
4. Look at `IMPLEMENTATION_COMPLETE.md` for detailed explanations

### Reporting Issues
Include:
- What you were doing
- What you expected to happen
- What actually happened
- Any error messages from console/browser DevTools

### Performance Tips
- Use fast USB 3.0 drives for better response
- Avoid accessing very large folders (10,000+ items)
- Clear thumbnail cache if disk fills up: Delete temp directory
- Restart app weekly for optimal performance

## Next Steps

1. ✅ **Setup complete** - First admin created
2. 📁 **Browse files** - Use file browser to navigate drives
3. 👤 **Add users** - Create accounts for team members
4. 🔐 **Manage permissions** - Promote key users to admin
5. 📚 **Review security** - Read SECURITY_IMPLEMENTATION.md
6. 🧪 **Test scenarios** - Follow TESTING_CHECKLIST.md
7. 🚀 **Deploy** - Follow deployment checklist in SECURITY_IMPLEMENTATION.md

## Quick Reference

| Task | Command/Location |
|------|------------------|
| Start app | `dotnet run` |
| Stop app | Ctrl+C |
| Login | http://localhost:5251 |
| Manage users | Click "Manage Users" (admin only) |
| Logout | Click "Logout" (top right) |
| Create admin | Visit /users.html (first time) |
| Reset users | Delete data/users.json + restart |
| View config | Open appsettings.json |
| Check auth | DevTools → Cookies (look for "aspnetcore.http.cookie") |

## Version Info
- **Framework**: ASP.NET Core 8.0 (net8.0)
- **Frontend**: Vanilla JavaScript
- **Database**: JSON file (data/users.json)
- **Port**: http://0.0.0.0:5251
- **Auth**: Cookie-based with PBKDF2 hashing
- **Status**: Production-ready ✅

---

**Last Updated**: January 2025
**Security Level**: User authentication + role-based access control
**Build Status**: ✅ Passing (0 errors, 14 warnings)
