# FilePortal Security Implementation Summary

## Overview
FilePortal implements a complete user authentication and authorization system with role-based access control, persistent user storage, and first-run admin creation workflow.

## Architecture

### Authentication Flow
1. **Initial Request**: User visits `/` (redirected to login if unauthenticated)
2. **Login**: Credentials validated via `UserStoreService.Authenticate()`
   - Primary: Check against persistent user store (data/users.json)
   - Fallback: Check env var `Auth__Username` and `Auth__Password`
   - Fallback: Check config appsettings.json Auth section
3. **Role Claims**: Admin users receive `"Admin"` role claim
4. **Cookie**: 7-day HttpOnly cookie issued with role claims
5. **Authorization**: Protected endpoints check `[Authorize]` and `[Authorize(Roles="Admin")]`

### User Storage
- **File**: `data/users.json` (created on first admin setup)
- **Format**: Array of `UserRecord` objects
- **Persistence**: Thread-safe JSON serialization with lock protection
- **Password Hashing**: PBKDF2 with SHA256, 10,000 iterations
- **Salt**: Base64-encoded random bytes (cryptographically secure)

### FirstRun Setup
- **Detection**: GET `/api/users/init` returns `{ isEmpty: bool }`
- **Endpoint**: POST `/api/users/init` accepts username/password (AllowAnonymous)
- **Creates**: Single initial admin user (isAdmin=true)
- **Protection**: Endpoint rejects if users already exist
- **UI**: users.html shows setup form if store is empty

## Endpoints

### Authentication
- **POST /api/auth/login** (AllowAnonymous)
  - Form: username, password
  - Response: 200 OK with cookie, 401 if invalid
  
- **POST /api/auth/logout** [Authorize]
  - Clears cookie
  - Response: 200 OK
  
- **GET /api/auth/me** [Authorize]
  - Response: `{ user: "username", isAdmin: bool }`

### User Management
- **GET /api/users/init** (AllowAnonymous)
  - Response: `{ isEmpty: bool }`
  - Used for first-run detection
  
- **POST /api/users/init** (AllowAnonymous)
  - Form: username, password
  - Creates initial admin if store empty
  - Response: 201 Created, 400 if users exist
  
- **GET /api/users** [Authorize(Admin)]
  - Lists all users with isAdmin flag
  - Response: `[{ username: "user1", isAdmin: true }, ...]`
  
- **POST /api/users** [Authorize(Admin)]
  - Form: username, password, isAdmin (optional)
  - Creates new user
  - Response: 201 Created
  
- **DELETE /api/users/{username}** [Authorize(Admin)]
  - Removes user
  - Safeguards: Prevents removing self, last admin
  - Response: 204 No Content, 400 if protected
  
- **POST /api/users/{username}/promote** [Authorize(Admin)]
  - Sets isAdmin=true
  - Response: 200 OK, 400 if already admin
  
- **POST /api/users/{username}/demote** [Authorize(Admin)]
  - Sets isAdmin=false
  - Safeguards: Prevents demoting self, last admin
  - Response: 200 OK, 400 if protected

## Security Safeguards

### Server-Side (Backend)
1. **Last-Admin Protection**
   - DELETE endpoint checks `CountAdmins() <= 1` before removal
   - DEMOTE endpoint checks `CountAdmins() <= 1` before demotion
   - Returns 400 Bad Request with descriptive error

2. **Self-Protection**
   - DELETE endpoint prevents removing current user
   - DEMOTE endpoint prevents demoting current user
   - Returns 400 Bad Request with descriptive error

3. **First-Run Protection**
   - POST /api/users/init rejects if users exist
   - AllowAnonymous allows initial setup without existing credentials
   - Once populated, subsequent admin operations require authentication

4. **Password Hashing**
   - PBKDF2 with 10,000 iterations and random salt
   - Salt stored in UserRecord for verification
   - No plaintext passwords stored

### Client-Side (Frontend)
1. **UI Disabling** (users.html)
   - "Remove" button disabled for self and last admin
   - "Demote" button disabled for self and last admin
   - Prevent buttons visible only for admin users

2. **Confirmation Dialogs**
   - User removal requires confirmation
   - Prevents accidental deletion

3. **First-Run Form**
   - Shown only if store is empty (checked via GET /api/users/init)
   - Hides user list and normal add form during setup
   - Redirects to main app after initial admin creation

## Configuration

### Environment Variables (Recommended)
```
Auth__Username=admin
Auth__Password=your-secure-password
```

### appsettings.json (Fallback)
```json
{
  "Roots": { ... },
  "Auth": {
    "Username": "admin",
    "Password": "your-secure-password"
  }
}
```

### Hardcoded Defaults (Last Resort)
- Username: "maikel"
- Password: "Cafeta0823$"
- These are overridden if env vars or config are present

## Testing Workflow

### 1. First-Run Scenario
```
- Delete data/users.json
- Start application
- Visit http://localhost:5251/users.html
- See "Create Initial Admin" form (not user list)
- Create username "admin" with password "secure123"
- Redirect to main app
- Login with admin/secure123
```

### 2. User Management
```
- Login as admin
- Navigate to "Manage Users" from main app
- Add new user (non-admin)
- Promote user to admin
- Try to demote or remove last admin (blocked with error)
- Remove non-admin user (succeeds)
```

### 3. Last-Admin Protection Verification
```
- Create 2 admin users (admin1, admin2)
- Try to demote admin1 (fails - both are admins, can demote)
- Demote admin2 (succeeds - now only admin1 is admin)
- Try to demote admin1 (fails - only admin, cannot demote)
- Try to remove admin1 (fails - only admin, cannot remove)
```

### 4. Self-Protection Verification
```
- Login as admin1
- Go to Manage Users
- "Remove" button disabled for admin1 (current user)
- "Demote" button disabled for admin1 (current user)
- Switch to another admin user account
- "Remove" button for admin1 now enabled
```

## Error Messages

### Common Scenarios
| Action | Condition | Response |
|--------|-----------|----------|
| Create initial admin | Users exist | 400: "Users already exist." |
| Delete user | Self | 400: "Cannot delete yourself." |
| Delete user | Last admin | 400: "Cannot remove the last admin." |
| Demote user | Self | 400: "Cannot demote yourself." |
| Demote user | Last admin | 400: "Cannot demote the last admin." |
| Promote user | Already admin | 400: "User is already an admin." |

## File Browsing Authorization
- All file endpoints require `[Authorize]` attribute
- Unauthenticated requests redirected to login
- API JSON error responses (not HTML redirect)
- Users must login before accessing file browser

## Implementation Files

### Backend
- **Services/UserStoreService.cs**: Core persistence and authentication
- **Controllers/AuthController.cs**: Login/logout endpoints
- **Controllers/UsersController.cs**: User management API
- **Models/UserRecord.cs**: User data model
- **Program.cs**: DI and middleware configuration

### Frontend
- **wwwroot/index.html**: Main file browser (requires login)
- **wwwroot/users.html**: User management (admin only)
- **wwwroot/login.html**: Login form (referenced by middleware)

### Configuration
- **appsettings.json**: Root definitions and credential fallback
- **Properties/launchSettings.json**: Server port and launch settings

## Build & Run

```bash
# Restore dependencies
dotnet restore

# Build
dotnet build

# Run (listen on http://0.0.0.0:5251)
dotnet run

# Visit
http://localhost:5251
```

## Known Limitations

1. **ImageSharp Vulnerabilities**: Package 3.1.0 has known CVEs. Consider upgrading when version with fixes is available.
2. **No User Audit Logging**: User creation/deletion events not logged. Could be added to UserStoreService.
3. **No Password Reset**: No mechanism for users to reset their own passwords.
4. **No Session Management**: No ability to revoke specific sessions or view active sessions.
5. **No 2FA**: Single-factor authentication only.

## Future Enhancements

1. Password reset via email
2. Session management and revocation
3. User audit log
4. Two-factor authentication
5. OAuth/OpenID Connect integration
6. Customizable password policy
7. Account lockout after failed attempts
