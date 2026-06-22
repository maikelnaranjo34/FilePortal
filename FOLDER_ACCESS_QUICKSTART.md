# Folder Access Control - Quick Reference

## For Admins

### Step 1: Create a User with Limited Access
1. Go to User Management (`http://localhost:8080/users.html`)
2. Under "Add User" section, fill in:
   - Username: `john`
   - First Name: `John`
   - Last Name: `Doe`
   - Password: (secure password)
   - Leave "Make admin" unchecked for regular user
3. Click "Add"

### Step 2: Assign Folders to User
1. Find the newly created user in the list
2. Click the **"Folders"** button
3. A modal appears showing:
   - **Available Roots** (top section): Check which drives/folders this user can access
   - **Assigned Folders** (bottom section): Shows what's already assigned
4. Check the folders you want to grant access to (e.g., "D", "E", "Samsung S20")
5. Click "Save"

### Example Scenarios

**Scenario 1: User can only see Samsung S20 phone**
- Unchecked: D, E, H, J, K, Data, Easystore
- Checked: Samsung S20
- Result: User sees only Samsung S20 in sidebar

**Scenario 2: User can see D and E drives only**
- Checked: D, E
- Unchecked: All others
- Result: User can browse D:\ and E:\ only

**Scenario 3: User has no access (sandbox)**
- Unchecked: All folders
- Result: User logs in but cannot browse anything

## For Regular Users

### What I Can See
- Your sidebar shows only folders your admin assigned to you
- If no folders assigned: sidebar is empty, you can't browse anything

### What Happens If I Try to Access Unauthorized Folders
- Navigating to forbidden path → Error: "You do not have access to this folder"
- This is enforced on both frontend and backend

### Share Drives
- Only displays in sidebar if your admin granted access
- Even if you somehow get the path, access is blocked

## API Reference (For Developers)

### Get User's Allowed Folders
```bash
GET /api/users/{username}/folders
Authorization: Cookie (admin required)

Response:
{
  "username": "john",
  "folders": ["D:\\", "E:\\Shared"]
}
```

### Set User's Allowed Folders
```bash
POST /api/users/{username}/folders
Authorization: Cookie (admin required)
Content-Type: application/json

Body:
["D:\\", "E:\\", "G:\\Data"]

Response:
{
  "message": "Allowed folders updated for john",
  "folders": ["D:\\", "E:\\", "G:\\Data"]
}
```

### Get Current User Info (Including Access)
```bash
GET /api/auth/me
Authorization: Cookie

Response:
{
  "user": "john",
  "isAdmin": false,
  "allowedFolders": ["D:\\", "E:\\"]
}
```

## Troubleshooting

**Problem**: User doesn't see any folders in sidebar after login  
**Solution**: Admin hasn't assigned folders to user. Go to User Management → click Folders → assign at least one.

**Problem**: Admin can see all folders but assigned user can't see assigned ones  
**Solution**: User needs to refresh the page or logout/login again. Changes take effect on next page load.

**Problem**: Path validation failed when saving folders  
**Solution**: Make sure the folder path actually exists on the filesystem. Paths must be valid Windows paths (e.g., D:\, E:\Share, not D: without backslash).

## Admin Privileges

**Admins always have full access** regardless of AllowedFolders setting:
- All roots visible in sidebar
- Can browse entire filesystem
- Cannot be restricted by folder control
- Can assign folders to other users

**To create a regular (non-admin) user**: Uncheck "Make admin" when adding user.

## Default Behavior (Migration)

If you're upgrading from an old version:
- Existing admin users: **No change**, retain full access
- Existing regular users: Have `AllowedFolders = []` (empty = no access)
  - Admin must assign folders to restore their access

To grant a regular user full access: Assign all roots in the Folders modal.
