# Folder Access Control Implementation

## Overview
Added per-user folder-level access control to FilePortal. Admins can now assign specific folders to individual users, restricting their access to only those designated folders.

## Key Changes

### 1. **UserRecord Model** ([Models/UserRecord.cs](Models/UserRecord.cs))
- Added `AllowedFolders: List<string>` property to store folder paths each user can access
- If list is empty:
  - **Admins**: Can access all folders (backward compatible)
  - **Regular users**: Have no access

### 2. **UserStoreService** ([Services/UserStoreService.cs](Services/UserStoreService.cs))
- Added `SetAllowedFolders(username, folderPaths)` method to update user's folder permissions
- Persists to `data/users.json`

### 3. **FilesController** ([Controllers/FilesController.cs](Controllers/FilesController.cs))
- Injected `UserStoreService` dependency
- Added `HasAccessToPath(path)` helper method:
  - Returns `true` if user is admin (full access)
  - Checks if path is within any of user's `AllowedFolders`
  - Normalizes paths for reliable comparison
- Updated `GetRoots()`: Filters visible roots based on user permissions
- Updated `GetFolder(path)`: Checks access before listing contents (returns 403 if denied)

### 4. **AuthController** ([Controllers/AuthController.cs](Controllers/AuthController.cs))
- Enhanced `/api/auth/me` endpoint to include:
  - `username`: Current authenticated user
  - `isAdmin`: Admin status
  - `allowedFolders`: List of accessible folders

### 5. **UsersController** ([Controllers/UsersController.cs](Controllers/UsersController.cs))
Added two new admin-only endpoints:

**POST** `/api/users/{username}/folders`
- Sets allowed folders for a user
- Validates folder paths exist on filesystem
- Returns normalized full paths
- Request body: `["D:\\", "E:\\Shared", ...]`

**GET** `/api/users/{username}/folders`
- Returns user's current folder permissions
- Response: `{ username, folders: [...] }`

### 6. **User Management UI** ([wwwroot/users.html](wwwroot/users.html))
Added folder access management interface:

**New Modal**: Folder Access Control
- Lists all available roots from `/api/files/roots`
- Shows checkboxes for root selection
- Displays assigned folders with remove buttons
- Admin-only access

**New Button**: "Folders" button next to each user
- Opens folder assignment modal
- Admins can assign/revoke folder access
- Changes are saved immediately

**Updated Styling**:
- Added `.folder-list` and `.folder-item` classes
- Modal uses z-index 1000 (above password modal)
- Responsive layout for folder selection

## How It Works

### Admin User Workflow
1. Navigate to User Management (`/users.html`)
2. Click "Folders" button next to a user
3. Check/uncheck root folders to assign access
4. Click "Save" - changes persist to `users.json`

### Regular User Access
1. User logs in
2. `/api/auth/me` returns `allowedFolders`
3. On file browse, only assigned roots appear in sidebar
4. Attempt to access unauthorized path returns `403 Forbidden`

### Data Persistence
- Folder assignments stored in `data/users.json`
- Example structure:
```json
{
  "username": "john",
  "isAdmin": false,
  "allowedFolders": ["D:\\", "E:\\Public", "G:\\"]
}
```

## Security Considerations

✅ **Path Normalization**: All paths normalized before comparison to prevent bypass attempts  
✅ **Permission Checks**: Every file operation checks user's folder access  
✅ **Admin Bypass**: Admins retain full access for backward compatibility  
✅ **Root Filtering**: Non-allowed roots hidden from UI  
✅ **Validation**: Only existing filesystem paths accepted

## API Endpoints Summary

| Endpoint | Method | Auth | Purpose |
|----------|--------|------|---------|
| `/api/files/roots` | GET | Yes | List accessible roots (filtered by permissions) |
| `/api/files/folder` | GET | Yes | List folder contents (403 if unauthorized) |
| `/api/users/{username}/folders` | GET | Admin | Get user's allowed folders |
| `/api/users/{username}/folders` | POST | Admin | Set user's allowed folders |
| `/api/auth/me` | GET | Yes | Get current user info + allowed folders |

## Testing the Feature

### Test Case 1: Admin Accessing All Folders
1. Login as admin
2. Verify all roots visible in sidebar
3. Can navigate any folder

### Test Case 2: Regular User with Limited Access
1. Create regular user "john"
2. Click "Folders" → assign only "D:\\" and "E:\\"
3. Login as john
4. Verify only D and E visible in sidebar
5. Attempt to access G:\\ → returns 403

### Test Case 3: User with No Access
1. Create regular user "restricted"
2. Click "Folders" → don't assign any
3. Login as restricted
4. Verify sidebar is empty
5. Cannot browse any folders

## Files Modified
- ✅ [Models/UserRecord.cs](Models/UserRecord.cs)
- ✅ [Services/UserStoreService.cs](Services/UserStoreService.cs)
- ✅ [Controllers/FilesController.cs](Controllers/FilesController.cs)
- ✅ [Controllers/AuthController.cs](Controllers/AuthController.cs)
- ✅ [Controllers/UsersController.cs](Controllers/UsersController.cs)
- ✅ [wwwroot/users.html](wwwroot/users.html)

## Backward Compatibility
✅ Existing admin users maintain full access  
✅ Existing `AllowedFolders` defaults to empty list  
✅ No breaking changes to existing API contracts
