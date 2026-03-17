# STAR-VP: Complete Quest Pro Data Collection Guide
# ================================================
# For: Meta Quest Pro 256GB + Unity 2022.3 LTS
# Purpose: Collect head tracking + eye tracking data for 360° viewport prediction
# Compatible with: Wu_MMSys_17 dataset format + PAVER pipeline
# Last updated: February 2026

---

## ⚠️ CRITICAL: WHY YOU MUST USE UNITY 2022.3 LTS (NOT 2021)

**DROP Unity 2021 LTS immediately.** Here's why:

| Problem with Unity 2021 LTS | Unity 2022.3 LTS Fix |
|---|---|
| Meta XR SDK v60+ won't install properly | Full Meta XR SDK support |
| "Oculus" XR Plugin is deprecated/renamed | Uses "Meta XR" plugin natively |
| `InputDevices.GetDevicesWithCharacteristics` eye tracking API is broken for Quest Pro | `OVREyeGaze` / `OVRPlugin` APIs work correctly |
| Android API level mismatches | Proper Android 12+ (API 32) support |
| OpenXR layer compatibility issues | Full OpenXR 1.0 support |
| XR Plug-in Management → "Oculus" checkbox missing or broken | Meta XR Feature Group works |
| IL2CPP build failures on ARM64 | Stable ARM64 builds |

**Recommended version: Unity 2022.3.22f1 LTS or later 2022.3.x patch**
(Check https://unity.com/releases/editor/qa/lts-releases for latest 2022.3 patch)

---

## PHASE 0: PREREQUISITES (Before Anything Else)

### Hardware Checklist
```
✅ Meta Quest Pro headset (256GB) — fully charged
✅ USB-C cable (the one that came with Quest Pro, or any USB 3.0+ data cable)
✅ Windows 10/11 PC (recommended) — macOS also works but Windows is simpler
   - 16GB RAM (8GB minimum but will be slow)
   - 20GB free disk space minimum
   - Any GPU with OpenGL 3.2+ (GTX 1060 or better recommended)
   - USB-C or USB-A port (with adapter if needed)
✅ Stable internet connection (for initial downloads only)
✅ Your 360° test videos (MP4, H.264, equirectangular projection)
```

### Accounts Needed (All Free)
```
1. Meta account — https://auth.meta.com/ (or use existing Facebook/Instagram)
2. Meta Developer account — https://developer.oculus.com/ (free, takes 2 min)
3. Unity account — https://id.unity.com/ (free Personal license)
```

### Software Download List (Do All Downloads First)
```
1. Unity Hub — https://unity.com/download
2. Unity 2022.3.22f1 LTS — installed through Unity Hub (see Phase 1)
3. SideQuest — https://sidequestvr.com/setup-howto (for easy file transfer)
4. Meta Quest Developer Hub (optional) — https://developer.oculus.com/meta-quest-developer-hub/
```

---

## PHASE 1: QUEST PRO SETUP (30 minutes)

### Step 1.1: Initial Quest Pro Setup
```
1. Charge Quest Pro to 100% (use charging dock or USB-C)
2. Download "Meta Quest" app on your phone (iOS App Store or Google Play)
3. Create/login to your Meta account in the phone app
4. Turn on Quest Pro (power button on right side)
5. Follow the in-headset setup wizard:
   - Pair with phone app (enter the code shown in headset)
   - Connect to WiFi
   - Set up guardian boundary
   - Complete initial tutorials
6. UPDATE FIRMWARE: Settings → System → Software Update → Install all updates
   (This is critical — old firmware = broken eye tracking)
```

### Step 1.2: Enable Eye Tracking on Quest Pro
```
In the Quest Pro headset:
1. Settings (gear icon) → Movement Tracking
2. Turn ON "Eye Tracking"
3. Turn ON "Natural Facial Expressions" (this enables face tracking too)
4. Calibrate Eye Tracking:
   - Settings → Movement Tracking → Eye Tracking → Calibrate
   - Follow the dots with your eyes
   - Must get "Good" or "Excellent" result
   - If "Poor": clean lenses, adjust headset fit, retry
5. Note your IPD setting (shown during calibration or at Settings → Display)
```

### Step 1.3: Enable Developer Mode (CRITICAL)
```
On your COMPUTER:
1. Go to https://developer.oculus.com/
2. Log in with same Meta account
3. Go to https://developer.oculus.com/manage/organizations/create/
4. Create organization: name it anything (e.g., "MyResearch")
5. Accept Developer Non-Disclosure Agreement
6. Accept Developer Agreement

On your PHONE (Meta Quest app):
1. Open Meta Quest app
2. Tap "Menu" → "Devices"
3. Select your Quest Pro
4. Tap "Developer Mode"
5. Toggle Developer Mode ON
   → If toggle doesn't appear: close app, wait 5 min, reopen
   → If still missing: verify developer account at developer.oculus.com
6. RESTART Quest Pro: Hold power button → Restart

VERIFY: Put on Quest Pro — you should see "Developer Mode" text
        in the bottom-right of your home environment
```

---

## PHASE 2: INSTALL UNITY 2022.3 LTS (1 hour)

### Step 2.1: Install Unity Hub
```
1. Download from https://unity.com/download
2. Run installer:
   - Windows: UnityHubSetup.exe → Next → Next → Install
   - Mac: UnityHub.dmg → drag to Applications
3. Open Unity Hub
4. Sign in with your Unity account (create free one if needed)
5. Activate "Personal" license (free, no credit card)
```

### Step 2.2: Install Unity Editor 2022.3 LTS
```
In Unity Hub:
1. Click "Installs" in left sidebar
2. Click "Install Editor" button (top right)
3. Find "Unity 2022.3.x LTS" — choose the LATEST 2022.3 patch
   (e.g., 2022.3.22f1 or higher)
   ⚠️ Do NOT pick 2021.x or 2023.x or Unity 6

4. Click "Install" next to it

5. In the "Add modules" dialog, CHECK these boxes:
   ☑ Android Build Support
      ├─☑ Android SDK & NDK Tools
      └─☑ OpenJDK
   ☑ Documentation (optional)

   UNCHECK everything else (save space):
   ☐ iOS Build Support
   ☐ WebGL Build Support
   ☐ Windows/Mac Build Support (already included)
   ☐ Linux Build Support

6. Click "Continue" → Accept all licenses → Click "Install"
7. Wait 30-60 minutes (downloads ~6GB total)
```

### Step 2.3: Verify Android Tools
```
After Unity install completes:
1. In Unity Hub → Installs → Click gear icon next to 2022.3
2. Click "Add Modules"
3. Verify these are checked (installed):
   ☑ Android Build Support
   ☑ Android SDK & NDK Tools
   ☑ OpenJDK

If any are missing, check them and click "Install"
```

---

## PHASE 3: CREATE UNITY PROJECT (30 minutes)

### Step 3.1: Create New Project
```
In Unity Hub:
1. Click "Projects" in left sidebar
2. Click "New Project" (top right)
3. Settings:
   - Editor Version: 2022.3.x (the one you just installed)
   - Template: "3D (Built-in Render Pipeline)"
     ⚠️ NOT "3D URP" or "3D HDRP" — just plain "3D"
   - Project Name: "QuestProDataCollector"
   - Location: Choose a folder with lots of space
     (e.g., D:\UnityProjects\ or ~/UnityProjects/)
4. Click "Create Project"
5. Wait 2-5 minutes for Unity to open
```

### Step 3.2: Switch to Android Platform
```
In Unity Editor:
1. Top menu: File → Build Settings
2. In Platform list, click "Android"
3. Click "Switch Platform" (bottom right)
   - Wait 5-10 minutes (progress bar at bottom of Unity)
   - When done: Unity icon appears next to "Android" ✅
4. Keep Build Settings window open
```

### Step 3.3: Configure Player Settings for Quest Pro
```
Still in Build Settings, click "Player Settings..." (bottom left):

In Inspector panel that opens:

A) "Player" section (top):
   - Company Name: "ResearchLab" (or your name)
   - Product Name: "VRDataCollector"

B) Expand "Other Settings":
   - Color Space: "Linear" (NOT Gamma)
   - Auto Graphics API: UNCHECK this
   - Graphics APIs: Remove "Vulkan" if present, keep only "OpenGLES3"
     (Click Vulkan → click minus button to remove)
   - Package Name: "com.research.vrdatacollector"
   - Minimum API Level: "Android 10.0 (API Level 29)"
   - Target API Level: "Automatic (highest installed)"
   - Scripting Backend: "IL2CPP" (NOT Mono)
   - Target Architectures: ☑ ARM64, ☐ ARMv7 (uncheck ARMv7!)
   - Internet Access: "Require" (for initial setup, can change later)

C) Expand "Publishing Settings":
   - Leave defaults (Unity will auto-create debug keystore)

Close Player Settings (just click away from Inspector)
Close Build Settings window
```

### Step 3.4: Install Meta XR SDK via Package Manager
```
Method A — Asset Store (RECOMMENDED, most reliable):

1. Open browser, go to:
   https://assetstore.unity.com/packages/tools/integration/meta-xr-all-in-one-sdk-269657

2. Click "Add to My Assets" (log in with Unity account if needed)

3. Back in Unity: Window → Package Manager

4. In Package Manager:
   - Click "Packages: In Project" dropdown (top-left)
   - Change to "My Assets"
   - Find "Meta XR All-in-One SDK"
   - Click "Download" then "Import"
   - In import dialog: click "All" then "Import"
   - Wait 5-10 minutes

5. Unity may show "Project Settings Update" dialog:
   - Click "Fix All" or "Apply All"
   - This configures OpenXR and Meta settings automatically

6. If prompted to restart Unity, click "Yes"


Method B — If Asset Store doesn't work:

1. Window → Package Manager
2. Click "+" (top left) → "Add package by name..."
3. Enter: com.meta.xr.sdk.all
4. Click "Add"
5. Wait for import (5-10 min)
6. If error: try adding these individually:
   - com.meta.xr.sdk.core
   - com.meta.xr.sdk.interaction
   - com.meta.xr.sdk.platform
```

### Step 3.5: Configure XR for Quest Pro
```
After Meta XR SDK is imported:

1. Edit → Project Settings

2. Left panel: Click "XR Plug-in Management"
   - If not visible: the Meta XR import should have added it
   - Click "Install XR Plug-in Management" if you see that button

3. Select the ANDROID tab (small Android robot icon at top)
   ☑ Check "Oculus" (or "Meta XR" depending on SDK version)

4. Click the small arrow next to "Oculus" to expand settings:
   - Stereo Rendering Mode: "Multiview"
   - Target Devices: ☑ Quest Pro (make sure it's checked!)
   - Also check ☑ Quest 2 and ☑ Quest 3 for compatibility

5. Left panel: Click "Oculus" (under XR Plug-in Management)
   - Low Overhead Mode: OFF (for development)
   - Optimize Buffer Discards: ON

6. Close Project Settings

7. File → Save Project (Ctrl+S)
```

### Step 3.6: Add OVR Camera Rig (Meta's VR Camera)
```
There are two ways to add the VR camera. Use Method A:

Method A — Using Building Blocks (Unity 2022.3 + Meta XR SDK v60+):

1. Top menu: Meta → Tools → Building Blocks
   (If not visible: Window → Meta → Building Blocks)

2. In Building Blocks window, click:
   - "Camera Rig" → Click "+" to add to scene
   - This adds OVRCameraRig to your scene

3. If Building Blocks is not available, use Method B:

Method B — Manual Setup:

1. In Hierarchy panel, DELETE the existing "Main Camera"
   (Right-click → Delete)

2. Right-click in Hierarchy → Create Empty
   - Name it "OVRCameraRig"

3. Select OVRCameraRig, in Inspector click "Add Component"
   - Search "OVR Camera Rig" → add it
   - Search "OVR Manager" → add it

4. On the OVR Manager component, set:
   - Tracking Origin Type: "Floor Level"
   - ☑ Use Recommended MSAA Level
   - Eye Tracking Support: "Required"
   - Face Tracking Support: "Supported"
   - Body Tracking Support: "None"
   - Hand Tracking Support: "Controllers Only"
   - ☑ Request Eye Tracking Permission On Startup
   - ☑ Request Face Tracking Permission On Startup

5. The OVRCameraRig will auto-create child objects:
   OVRCameraRig
   ├── TrackingSpace
   │   ├── CenterEyeAnchor (this has the Camera component)
   │   ├── LeftEyeAnchor
   │   ├── RightEyeAnchor
   │   ├── LeftHandAnchor
   │   └── RightHandAnchor

6. Select CenterEyeAnchor: verify it has a Camera component
```

---

## PHASE 4: CREATE 360° VIDEO DISPLAY (30 minutes)

### Step 4.1: Create Inside-Out Sphere for 360° Video
```
1. Hierarchy → Right-click → 3D Object → Sphere
2. Rename to "VideoSphere"
3. Select VideoSphere, set in Inspector:
   - Transform Position: (0, 0, 0)
   - Transform Rotation: (0, 0, 0)
   - Transform Scale: (100, 100, 100)
4. On the Mesh Renderer component:
   - Uncheck "Cast Shadows"
   - Uncheck "Receive Shadows"
```

### Step 4.2: Create Video Material
```
1. In Project panel (bottom), right-click in Assets folder
2. Create → Folder → name "Materials"
3. Inside Materials folder: Right-click → Create → Material
4. Name it "VideoMaterial"
5. Select VideoMaterial, in Inspector:
   - Click the "Shader" dropdown (currently says "Standard")
   - Change to: "Unlit/Texture"
6. Drag VideoMaterial from Project panel onto VideoSphere in Hierarchy
```

### Step 4.3: Create Scripts Folder
```
1. In Project panel, right-click in Assets → Create → Folder
2. Name: "Scripts"
```

### Step 4.4: Create InvertSphere Script
```
1. In Assets/Scripts: Right-click → Create → C# Script
2. Name: "InvertSphere"
3. Double-click to open in code editor
4. Replace ALL contents with the script from: InvertSphere.cs (see SCRIPTS section below)
5. Save the file (Ctrl+S)
6. Go back to Unity (wait for compilation — bottom-right shows spinner)
7. Drag "InvertSphere" script onto VideoSphere in Hierarchy
```

### Step 4.5: Add Video Player to Sphere
```
1. Select VideoSphere in Hierarchy
2. Inspector → Add Component → search "Video Player" → add it
3. Configure Video Player:
   - Source: "URL" (we'll load videos from Quest storage)
   - Render Mode: "Material Override"
   - Renderer: drag VideoSphere here (or it auto-fills)
   - Material Property: "_MainTex"
   - Play On Awake: ☐ UNCHECK
   - Loop: ☐ UNCHECK
   - Skip On Drop: ☑ CHECK
   - Playback Speed: 1
```

---

## PHASE 5: ADD EYE TRACKING COMPONENTS (20 minutes)

### Step 5.1: Add OVREyeGaze Components
```
Quest Pro eye tracking uses OVREyeGaze components. Add them to the camera rig:

1. In Hierarchy, expand OVRCameraRig → TrackingSpace

2. Select "LeftEyeAnchor"
   - Inspector → Add Component → search "OVR Eye Gaze"
   - Set: Eye: "Left"
   - ☑ Apply Position
   - ☑ Apply Rotation

3. Select "RightEyeAnchor"
   - Inspector → Add Component → search "OVR Eye Gaze"
   - Set: Eye: "Right"
   - ☑ Apply Position
   - ☑ Apply Rotation

4. Select "CenterEyeAnchor"
   - Inspector → Add Component → search "OVR Eye Gaze"
   - Set: Eye: "Combined" (if available)
   - If "Combined" not available, leave it — we'll compute it in code
```

### Step 5.2: Set Android Manifest Permissions
```
The Meta XR SDK should auto-configure permissions, but verify:

IMPORTANT: The Eye Tracking and Face Tracking settings are NOT in Project Settings.
They are located on the OVR Manager component in the Inspector panel.

Method 1 — Configure via OVR Manager Component (RECOMMENDED):

1. In the Hierarchy panel, find and select "OVRCameraRig"
   - If you don't see OVRCameraRig: go back to Step 3.6 and add it first
   - If you used Building Blocks: OVRCameraRig should already exist in your scene

2. With OVRCameraRig selected, look at the Inspector panel (usually on the right side)
   - Scroll down to find the "OVR Manager" component
   - If you don't see OVR Manager: click "Add Component" → search "OVR Manager" → add it

3. In the OVR Manager component, find these settings:
   - "Eye Tracking Support" dropdown → Set to: "Required"
   - "Face Tracking Support" dropdown → Set to: "Supported"
   - "Body Tracking Support" dropdown → Set to: "None" (unless you need body tracking)
   - "Hand Tracking Support" dropdown → Set to: "Controllers Only" (or "Both" if you need hand tracking)

4. Also ensure these checkboxes are enabled:
   - ☑ "Request Eye Tracking Permission On Startup"
   - ☑ "Request Face Tracking Permission On Startup"

5. If you still can't see these options:
   - Make sure Meta XR SDK is properly installed (Step 3.4)
   - Check that you're using Unity 2022.3 LTS (not 2021)
   - Try restarting Unity after installing Meta XR SDK
   - Verify the OVR Manager component is the latest version (should show version number in Inspector)

Method 2 — Verify via Android Manifest (Alternative Check):

If you want to verify the permissions are set correctly in the Android manifest:

1. Go to: Assets/Plugins/Android/AndroidManifest.xml
   - If this file doesn't exist: that's normal — Meta SDK creates it at build time
   - If it exists, open it and look for these entries:

2. Required permissions should include:
   <uses-permission android:name="com.oculus.permission.EYE_TRACKING" />
   <uses-feature android:name="com.oculus.feature.EYE_TRACKING" />
   <uses-feature android:name="oculus.software.eye_tracking" android:required="true" />

3. If the manifest file doesn't exist yet:
   - This is normal — Meta XR SDK will auto-generate it with correct permissions when you build
   - The OVR Manager settings (Method 1) are what actually control the manifest generation

TROUBLESHOOTING:

Q: I don't see "OVR Manager" component when I select OVRCameraRig
A: 
   - Click "Add Component" button in Inspector
   - Search for "OVR Manager" (not "OVRManager" — use the space)
   - If still not found: re-import Meta XR SDK (Window → Package Manager → My Assets → Meta XR All-in-One SDK → Remove → then Import again)

Q: I see OVR Manager but "Eye Tracking Support" dropdown is missing
A:
   - Your Meta XR SDK version might be too old
   - Update via Package Manager: Window → Package Manager → My Assets → Meta XR All-in-One SDK → Update
   - Or re-import the latest version from Asset Store

Q: The dropdown shows "None" but I can't change it to "Required"
A:
   - Make sure you're targeting Android platform: File → Build Settings → Android
   - Check that Quest Pro is selected in XR Plug-in Management: Edit → Project Settings → XR Plug-in Management → Android tab → Oculus → Target Devices → ☑ Quest Pro

Q: I followed Step 3.6 already — do I need to do this again?
A:
   - If you already set Eye Tracking Support = "Required" and Face Tracking Support = "Supported" in Step 3.6, you're done!
   - Step 5.2 is just a verification step to make sure those settings are still correct
```

---

## PHASE 6: CREATE ALL C# SCRIPTS (1-2 hours)

### Overview: You Need These 4 Scripts
```
Assets/Scripts/
├── InvertSphere.cs          — Flips sphere normals (view from inside)
├── DataCollector.cs          — Main data collection (head + eye + combined)
├── VideoManager.cs           — Loads & sequences 360° videos
└── ParticipantSetup.cs       — UI for entering participant ID
```

### IMPORTANT: Copy these scripts EXACTLY as provided below.
### Do NOT modify them unless you understand the changes.

---

## PHASE 7: SET UP THE SCENE (30 minutes)

### Step 7.1: Create DataCollectionManager
```
1. Hierarchy → Right-click → Create Empty
2. Name: "DataCollectionManager"
3. Position: (0, 0, 0)

4. Add DataCollector script:
   - Inspector → Add Component → DataCollector
   - VR Camera: Drag "CenterEyeAnchor" from OVRCameraRig/TrackingSpace
   - Video Player: Drag "VideoSphere" from Hierarchy
   - OVR Camera Rig: Drag "OVRCameraRig" from Hierarchy
   - Participant ID: "TEST" (default for testing)
   - Video ID: "test_video"
   - Record Data: ☑ checked

5. Add VideoManager script:
   - Inspector → Add Component → VideoManager
   - Video Player: Drag "VideoSphere" from Hierarchy
   - Data Collector: Drag "DataCollectionManager" (itself) from Hierarchy
```

### Step 7.2: Create Participant Setup Scene (Optional but Recommended)
```
For now, skip this — you can set participant ID in the Inspector.
When ready for real collection, create a separate scene with UI.
See ParticipantSetup.cs script for the implementation.
```

### Step 7.3: Save Everything
```
1. File → Save (Ctrl+S) → Save scene as "DataCollection" in Assets/Scenes/
2. File → Save Project
```

---

## PHASE 8: PREPARE VIDEOS ON QUEST PRO (20 minutes)

### Step 8.1: Connect Quest Pro to Computer
```
1. Turn on Quest Pro, put it on
2. Connect USB-C cable: Quest Pro → Computer
3. In Quest Pro, you'll see popup:
   "Allow access to data?"
   → Tap "Allow"
   "Allow USB debugging?"
   → Check "Always allow from this computer"
   → Tap "OK"
```

### Step 8.2: Copy Videos to Quest Pro
```
On your computer:

Windows:
1. Open File Explorer
2. You should see "Quest Pro" as a device
3. Navigate to: Quest Pro → Internal Storage
4. Create folder: Movies/VRStudy/
5. Copy your 360° videos into Movies/VRStudy/
   Name them clearly:
   - video_0.mp4  (matches Wu_MMSys_17 naming)
   - video_1.mp4
   - video_2.mp4
   - ... etc.

Linux:
1. Install android-tools (apt install android-tools-adb)
2. Or use:
   adb push /path/to/your/videos/ /sdcard/Movies/VRStudy/

Mac:
1. Install Android File Transfer (https://www.android.com/filetransfer/)
2. Or use adb (install via Homebrew: brew install android-platform-tools)
3. adb push /path/to/your/videos/ /sdcard/Movies/VRStudy/
```

### Step 8.3: Configure Video List in Unity
```
1. In Unity, select DataCollectionManager
2. In VideoManager component:
   - Video URLs list: Add entries like:
     file:///sdcard/Movies/VRStudy/video_0.mp4
     file:///sdcard/Movies/VRStudy/video_1.mp4
     ... etc.

   - Video IDs list: Add matching IDs:
     video_0
     video_1
     ... etc.

   IMPORTANT: Video URLs and Video IDs lists must have same count!
```

---

## PHASE 9: BUILD AND DEPLOY (30 minutes)

### Step 9.1: Build Settings
```
1. File → Build Settings
2. Click "Add Open Scenes" to add DataCollection scene
3. Verify:
   - Platform: Android (Unity icon next to it)
   - Texture Compression: ASTC
   - Run Device: Your Quest Pro should appear in dropdown
     → If not: unplug/replug USB, or click "Refresh"
   - Development Build: ☑ CHECK (for testing)
   - Script Debugging: ☑ CHECK (for testing)
4. Click "Build And Run"
5. Save as: "VRDataCollector.apk" in a "Builds" folder
6. Wait 5-15 minutes for first build
```

### Step 9.2: First Launch Permissions
```
When app launches on Quest Pro:
1. You'll see permission dialogs:
   - "Allow eye tracking?" → ALLOW
   - "Allow data access?" → ALLOW
2. These only appear once per install

If you miss them:
   - Quest Settings → Apps → VRDataCollector → Permissions
   - Enable all permissions manually
```

### Step 9.3: Test Data Collection
```
1. Put on Quest Pro
2. App should launch automatically after build
3. Video should start playing inside the sphere
4. Move your head around — you're looking inside the 360° video
5. Let it play for 30 seconds
6. Take off headset
7. The app writes data while running; it saves when video ends or app closes
```

### Step 9.4: Retrieve Test Data
```
Method A — ADB Command Line:

1. Open terminal/command prompt on computer

2. Check Quest Pro is connected:
   adb devices
   (Should show a device ID)

3. List data files:
   adb shell ls /sdcard/Android/data/com.research.vrdatacollector/files/DataCollection/

4. Copy all data to computer:
   adb pull /sdcard/Android/data/com.research.vrdatacollector/files/DataCollection/ ./QuestData/

5. Open CSV files and verify:
   - Timestamps incrementing
   - Head quaternion values changing
   - Eye gaze direction values present (not all zeros)
   - File has thousands of rows (90 Hz × seconds)


Method B — SideQuest (easier GUI):

1. Open SideQuest on computer
2. Connect Quest Pro
3. Click file browser icon
4. Navigate to: /sdcard/Android/data/com.research.vrdatacollector/files/DataCollection/
5. Download CSV files
```

---

## PHASE 10: REAL DATA COLLECTION PROTOCOL

### Per-Participant Procedure
```
BEFORE participant arrives:
□ Quest Pro charged >90%
□ Lenses cleaned with microfiber cloth
□ Facial interface cleaned/sanitized
□ Test run done (yourself, 30 seconds)
□ Previous participant data backed up
□ Verify videos loaded: adb shell ls /sdcard/Movies/VRStudy/

WHEN participant arrives:
□ Consent form signed
□ Pre-study questionnaire completed (paper or tablet)
□ Assign participant ID: P001, P002, etc.

HEADSET SETUP for participant:
□ Adjust top strap for comfort
□ Adjust IPD slider for their eyes
□ Calibrate eye tracking:
   Settings → Movement Tracking → Eye Tracking → Calibrate
   Must get "Good" or "Excellent"
□ Participant confirms image is clear

DATA COLLECTION:
□ Launch VRDataCollector app (from Quest app library → "Unknown Sources")
□ In Inspector (or code): set participant ID
□ Videos play automatically in sequence
□ Monitor participant for discomfort (take breaks if needed)
□ All videos complete → app stops recording

POST-COLLECTION:
□ Remove headset
□ Post-session questionnaire
□ Connect Quest Pro to computer
□ Run backup script (see below)
□ Verify CSV files exist and have data
□ Clean headset for next participant
```

---

## COLLECTED DATA FORMAT

### Head Tracking CSV (matches Wu_MMSys_17 format + extras)
```
Columns:
Timestamp,PlaybackTime,UnitQuaternion.x,UnitQuaternion.y,UnitQuaternion.z,UnitQuaternion.w,HmdPosition.x,HmdPosition.y,HmdPosition.z,EulerYaw,EulerPitch,EulerRoll,VelYaw,VelPitch,VelRoll

Example row:
0.0111,0.0111,-0.0150,0.9480,-0.0630,-0.3100,-0.1600,1.1160,-0.2590,245.12,-3.45,0.12,2.34,-0.56,0.01

Note: First 9 columns EXACTLY match Wu_MMSys_17 format for compatibility.
Extra columns (Euler, Velocity) are bonus data for your model.
```

### Eye Tracking CSV (NEW data — Wu_MMSys_17 didn't have this!)
```
Columns:
Timestamp,PlaybackTime,GazeDir.x,GazeDir.y,GazeDir.z,GazeOrigin.x,GazeOrigin.y,GazeOrigin.z,GazeYaw,GazePitch,LeftPupilDiam,RightPupilDiam,LeftOpenness,RightOpenness,LeftConfidence,RightConfidence,IsFixating,FixationDurationMs,BothEyesValid

Example row:
0.0111,0.0111,0.1234,0.0567,-0.9890,0.0320,1.6500,0.0100,172.88,-3.26,3.45,3.52,0.95,0.97,0.98,0.99,1,156.3,1
```

### Combined Eye-Head CSV
```
Columns:
Timestamp,PlaybackTime,HeadYaw,HeadPitch,HeadRoll,GazeYaw,GazePitch,GazeRelativeH,GazeRelativeV,AvgPupil,EyeHeadOffset,AbsoluteGazeYaw,AbsoluteGazePitch

Example row:
0.0111,0.0111,245.12,-3.45,0.12,172.88,-3.26,-72.24,0.19,3.49,72.24,172.88,-3.26
```

---

## DATA FOLDER STRUCTURE (On Your Computer After Collection)

```
STAR_VP/
├── vr-dataset/
│   └── Formated_Data/         ← Existing Wu_MMSys_17 data
│       ├── Experiment_1/      ← 48 participants, 9 videos each
│       └── Experiment_2/      ← 48 participants, 9 videos each
│
├── quest-pro-data/            ← NEW: Your Quest Pro collected data
│   ├── Raw/
│   │   ├── P001/
│   │   │   ├── head_P001_video_0_20260216_143022.csv
│   │   │   ├── eye_P001_video_0_20260216_143022.csv
│   │   │   ├── combined_P001_video_0_20260216_143022.csv
│   │   │   ├── head_P001_video_1_20260216_143512.csv
│   │   │   ├── eye_P001_video_1_20260216_143512.csv
│   │   │   └── combined_P001_video_1_20260216_143512.csv
│   │   ├── P002/
│   │   └── ...
│   │
│   ├── Processed/             ← After running validation script
│   │   └── (generated after collection)
│   │
│   ├── VideoMetadata/
│   │   └── video_annotations.csv
│   │
│   ├── Questionnaires/
│   │   ├── pre_study/
│   │   └── post_session/
│   │
│   └── Backups/
│       ├── Backup_20260216/
│       └── ...
│
├── data/                      ← PAVER data directory
│   └── wu_mmsys_17/
│
└── PAVER/                     ← PAVER model code
```

---

## TROUBLESHOOTING

### Unity Build Errors
```
Error: "No Android devices found"
Fix: 
  1. Unplug/replug USB cable
  2. In Quest Pro: accept USB debugging dialog
  3. Run: adb devices (verify device shows)
  4. In Unity Build Settings: click "Refresh" next to Run Device

Error: "Gradle build failed"
Fix:
  1. Edit → Preferences → External Tools
  2. Verify Android SDK, NDK, JDK paths are set
  3. If blank: reinstall Android modules via Unity Hub

Error: "Meta XR SDK import failed"
Fix:
  1. Close Unity
  2. Delete Library/ folder in project directory
  3. Reopen Unity (will reimport everything)
  4. Try importing Meta XR SDK again

Error: "IL2CPP build error"
Fix:
  1. Player Settings → Other Settings → Scripting Backend: IL2CPP
  2. Target Architectures: only ARM64 (uncheck ARMv7)
  3. Clean build: Build Settings → "Clean Build" option
```

### Eye Tracking Not Working
```
Symptom: All eye tracking values are zero
Fix:
  1. Verify Quest Pro firmware is up to date
  2. Settings → Movement Tracking → Eye Tracking → ON
  3. Re-calibrate eye tracking
  4. In Unity: OVRManager → Eye Tracking Support = "Required"
  5. Rebuild and redeploy app
  6. When app launches: MUST accept eye tracking permission dialog

Symptom: Eye tracking works but pupil diameter is always 0
Note: Quest Pro's OVRPlugin does NOT directly expose pupil diameter
      in all SDK versions. The script handles this with a fallback.
      Pupil data availability depends on SDK version.
```

### Video Not Playing
```
Symptom: Black sphere / no video
Fix:
  1. Verify video file exists: adb shell ls /sdcard/Movies/VRStudy/
  2. Video format must be: MP4, H.264 codec, equirectangular
  3. Check URL in VideoManager matches exact filename
  4. URL must start with: file:///sdcard/ (three slashes!)
  5. Try a simple test video first (short, small file)
```

---

## SCRIPT FILES (Copy These Exactly)

See the separate .cs files in the UnityScripts/ folder:
- InvertSphere.cs
- DataCollector.cs
- VideoManager.cs
- ParticipantSetup.cs

Also see:
- validate_data.py — Python script to verify collected CSV data
- backup_quest_data.sh — Shell script for automated backups
