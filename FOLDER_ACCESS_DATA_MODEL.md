# Folder Access Control - Data Model & Database Changes

## UserRecord Model Update

### Before (Old Schema)
```csharp
public class UserRecord
{
    public string Username { get; set; }
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public string Salt { get; set; }          // base64
    public string Hash { get; set; }          // base64
    public DateTime CreatedUtc { get; set; }
    public bool IsAdmin { get; set; }
}
```

### After (New Schema)
```csharp
public class UserRecord
{
    public string Username { get; set; }
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public string Salt { get; set; }          // base64
    public string Hash { get; set; }          // base64
    public DateTime CreatedUtc { get; set; }
    public bool IsAdmin { get; set; }
    public List<string> AllowedFolders { get; set; } = new();  // NEW
}
```

## JSON Storage Format

### File Location
`data/users.json` (automatically created if it doesn't exist)

### Example: users.json with AllowedFolders

```json
[
  {
    "username": "maikel",
    "firstName": "Maikel",
    "lastName": "Naranjo",
    "salt": "base64EncodedSaltHere==",
    "hash": "base64EncodedHashHere==",
    "createdUtc": "2025-02-17T10:30:00Z",
    "isAdmin": true,
    "allowedFolders": []
  },
  {
    "username": "john",
    "firstName": "John",
    "lastName": "Doe",
    "salt": "base64EncodedSaltHere==",
    "hash": "base64EncodedHashHere==",
    "createdUtc": "2025-02-17T11:00:00Z",
    "isAdmin": false,
    "allowedFolders": [
      "D:\\",
      "E:\\",
      "D:\\Samsung S20_Cloud\\WebsiteBrowser\\Files"
    ]
  },
  {
    "username": "restricted",
    "firstName": "Restricted",
    "lastName": "User",
    "salt": "base64EncodedSaltHere==",
    "hash": "base64EncodedHashHere==",
    "createdUtc": "2025-02-17T11:30:00Z",
    "isAdmin": false,
    "allowedFolders": []
  }
]
```

## Access Control Logic

### Permission Evaluation (HasAccessToPath)

```
User requests path: "E:\Videos\MyVideo.mp4"

Step 1: Is user an admin?
  └─ YES → ✅ Access granted
  └─ NO  → Go to Step 2

Step 2: Get user from store
  └─ User not found → ❌ Access denied
  └─ User found → Go to Step 3

Step 3: Is AllowedFolders empty?
  └─ YES → ❌ Access denied (no folders assigned)
  └─ NO  → Go to Step 4

Step 4: Does requested path match any allowed folder?
  └─ Exact match (e.g., E:\ matches E:\)
     → ✅ Access granted
  └─ Path under allowed folder (e.g., E:\Videos under E:\)
     → ✅ Access granted
  └─ No match found
     → ❌ Access denied
```

## Path Normalization

All paths are normalized before comparison to prevent bypass attempts:

**Input Path**: `E:\Videos\../Photos`  
**Normalized**: `E:\Photos`  
**Comparison**: Checked against allowed folders

This prevents path traversal attacks like:
- `D:\..\..\E:\Restricted`
- `//../bypass/path`
- Mixed forward/backward slashes

## Migration from Older Versions

### If upgrading from version WITHOUT AllowedFolders

**Automatic Handling**:
1. Existing users.json will be loaded
2. Missing `AllowedFolders` property → Defaults to empty list `[]`
3. Users can continue logging in
4. Admin must assign folders to non-admin users

**Example Migration**:

Old user.json:
```json
[
  { "username": "maikel", "isAdmin": true },
  { "username": "john", "isAdmin": false }
]
```

After first load with new code:
```json
[
  { "username": "maikel", "isAdmin": true, "allowedFolders": [] },
  { "username": "john", "isAdmin": false, "allowedFolders": [] }
]
```

Then:
- `maikel` (admin): ✅ Can access all (admins bypass AllowedFolders)
- `john` (user): ❌ Cannot access anything until admin assigns folders

## Performance Considerations

- **Path Comparison**: O(n) where n = number of AllowedFolders per user
- **Typical case**: 1-10 allowed folders per user → negligible performance impact
- **File Listing**: One permission check per API call (no per-file overhead)
- **Caching**: User record loaded once per request (no caching layer needed)

## Security Notes

✅ **Path Normalization**: Prevents `..` and mixed slash traversal  
✅ **Exact Path Matching**: `E:\` ≠ `E:` (trailing slash matters)  
✅ **Admin Bypass**: Admins not restricted (intentional design)  
✅ **Case-Insensitive**: Windows drive letters are case-insensitive, paths compared case-sensitively  
✅ **Symbolic Links**: Not followed (prevents escaping allowed folders)  
✅ **UNC Paths**: Supported (e.g., `\\192.168.1.1\share`)

## API Response Changes

### Before: `/api/auth/me`
```json
{
  "user": "john"
}
```

### After: `/api/auth/me`
```json
{
  "user": "john",
  "isAdmin": false,
  "allowedFolders": ["D:\\", "E:\\"]
}
```

This allows frontend to:
- Display assigned folders to user (optional UI feature)
- Validate attempted navigation matches allowed folders
- Show helpful messages if access denied

## Database Backup Recommendations

Before upgrading to new version:
```powershell
# Backup existing users.json
Copy-Item data/users.json data/users.json.backup.$(Get-Date -Format 'yyyyMMdd_HHmmss')
```

To restore:
```powershell
# Restore from backup if needed
Copy-Item data/users.json.backup.20250217_100000 data/users.json
```

## Troubleshooting Data Issues

### Users.json Corrupt?
**Solution**: 
1. Stop the application
2. Delete or restore from backup: `data/users.json`
3. Restart application (will create fresh file on next login attempt)

### AllowedFolders Showing as null?
**Solution**: 
1. C# model has default `= new()` so this shouldn't happen
2. If it does: Stop app, edit users.json, change `"allowedFolders": null` to `"allowedFolders": []`
3. Restart app

### Can't Save Folder Permissions?
**Check**:
1. Folder path exists on filesystem: `Test-Path "D:\MyFolder"`
2. Application has permission to read path
3. User store file not locked by another process
4. Application logs for errors (check console output)
