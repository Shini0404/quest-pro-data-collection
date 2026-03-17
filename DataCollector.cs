// =============================================================================
// DataCollector.cs
// Purpose: Records head tracking + eye tracking + face tracking data from
//          Quest Pro at 90 Hz
// Attach to: DataCollectionManager (empty GameObject)
// Project: STAR-VP Quest Pro Data Collection
// Unity: 2022.3 LTS + Meta XR All-in-One SDK
//
// OUTPUT FORMAT:
//   Head CSV  — Wu_MMSys_17 compatible (Timestamp, PlaybackTime,
//               Quaternion, Position, Euler, Velocity)
//   Eye CSV   — Gaze direction/origin, openness (from face tracking), fixation
//   Face CSV  — All 63 facial expression blend weights (Quest Pro)
//   Combined  — Merged summary of head + eye + face for quick analysis
//
// NOTES ON QUEST PRO LIMITATIONS:
//   - Pupil diameter is NOT exposed by Meta XR SDK (v85). Columns set to -1.
//   - Eye openness uses face tracking EyesClosed blend shapes (not Confidence).
//   - PlaybackTime syncs to VideoPlayer.time whenever video is prepared.
//
// DEPENDENCIES:
//   - Meta XR Core SDK (OVRPlugin, OVRManager, OVREyeGaze)
//   - OVRCameraRig in scene with OVRManager configured for:
//       Eye Tracking Support:  "Required"
//       Face Tracking Support: "Supported" (or "Required")
//       ☑ Request Face Tracking Permission On Startup
// =============================================================================

using UnityEngine;
using UnityEngine.Video;
using System.IO;
using System;
using System.Text;
using System.Collections.Generic;

public class DataCollector : MonoBehaviour
{
    // =========================================================================
    // INSPECTOR REFERENCES — Drag these in the Unity Inspector
    // =========================================================================
    [Header("Scene References")]
    [Tooltip("Drag CenterEyeAnchor from OVRCameraRig/TrackingSpace here")]
    public Transform vrCamera;

    [Tooltip("Drag VideoSphere (which has VideoPlayer component) here")]
    public VideoPlayer videoPlayer;

    [Tooltip("Drag OVRCameraRig root object here")]
    public OVRCameraRig ovrCameraRig;

    // =========================================================================
    // SETTINGS — Configurable per participant/video
    // =========================================================================
    [Header("Session Settings")]
    [Tooltip("Set this for each participant: P001, P002, etc.")]
    public string participantID = "P001";

    [Tooltip("Current video ID — set by VideoManager automatically")]
    public string videoID = "video_0";

    [Tooltip("Check to start/stop recording — set by VideoManager when video plays")]
    public bool recordData = false;  // FIX: start as false; VideoManager enables when video is selected

    [Header("Advanced Settings")]
    [Tooltip("Write to disk every N frames (lower = safer but slower)")]
    public int flushInterval = 90; // Flush every ~1 second at 90 Hz

    [Tooltip("Fixation velocity threshold in degrees/second")]
    public float fixationThreshold = 30f;

    [Tooltip("Eye openness below this = blink (0-1 scale)")]
    public float blinkThreshold = 0.2f;

    // =========================================================================
    // PRIVATE STATE
    // =========================================================================
    private string basePath;
    private StreamWriter headTrackingFile;
    private StreamWriter eyeTrackingFile;
    private StreamWriter faceTrackingFile;
    private StreamWriter combinedFile;

    private float sessionStartTime;
    private int frameCount = 0;
    private bool isRecording = false;
    private bool filesOpen = false;
    private bool waitingForVideoStart = false; // true after files created, until video.isPlaying

    // Previous frame data for velocity/fixation calculation
    private Vector3 prevHeadEuler;
    private Vector3 prevGazeDir;
    private float prevTimestamp;
    private float fixationStartTime;
    private bool wasFixating = false;

    // String builder for performance (avoid GC allocations)
    private StringBuilder sb = new StringBuilder(2048);

    // Eye tracking state from OVRPlugin
    private OVRPlugin.EyeGazesState _eyeGazesState;

    // Face tracking state from OVRPlugin
    private OVRPlugin.FaceState _faceState;
    private bool faceTrackingAvailable = false;
    private int faceExpressionCount = 0;
    private string[] faceExpressionNames;

    // Cached indices for key face expressions (used in combined CSV + eye openness)
    private int faceIdx_SmileL = -1;
    private int faceIdx_SmileR = -1;
    private int faceIdx_BrowDownL = -1;
    private int faceIdx_BrowDownR = -1;
    private int faceIdx_JawDrop = -1;
    private int faceIdx_EyeClosedL = -1;
    private int faceIdx_EyeClosedR = -1;
    private int faceIdx_EyeLookDownL = -1;
    private int faceIdx_EyeLookDownR = -1;
    private int faceIdx_LidTightenerL = -1;
    private int faceIdx_LidTightenerR = -1;
    private int faceIdx_UpperLidRaiserL = -1;
    private int faceIdx_UpperLidRaiserR = -1;

    // =========================================================================
    // UNITY LIFECYCLE
    // =========================================================================

    /// <summary>
    /// Awake runs BEFORE Start() on all scripts. We load the participant ID here
    /// so that VideoManager.Start() can read the correct ID for the banner.
    /// </summary>
    void Awake()
    {
        // CRITICAL: Force recordData OFF at startup — VideoManager will enable it
        // when a video is selected. This prevents ghost recordings on launch.
        recordData = false;
        videoID = "";

        // =====================================================================
        // PARTICIPANT ID RESOLUTION (runs in Awake so it's ready before Start)
        //   1. Config file on device  (highest — no rebuild needed!)
        //   2. Inspector value        (baked into APK at build time)
        // =====================================================================
        string inspectorID = participantID;  // Save what was serialized

        string configPath = Path.Combine(Application.persistentDataPath, "participant_id.txt");
        string configID = "";
        bool configFileExists = File.Exists(configPath);
        if (configFileExists)
        {
            try
            {
                configID = File.ReadAllText(configPath).Trim();
                if (!string.IsNullOrEmpty(configID))
                {
                    participantID = configID;
                    Debug.Log($"[DataCollector] Awake: Loaded participant ID from CONFIG FILE: {participantID}");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DataCollector] Awake: Failed to read config file: {e.Message}");
            }
        }

        // Clear PlayerPrefs to prevent stale overrides from old sessions
        PlayerPrefs.DeleteKey("ParticipantID");
        PlayerPrefs.Save();

        Debug.Log($"[DataCollector] Awake: Participant = {participantID} (inspector={inspectorID}, config={configID})");
    }

    void Start()
    {
        // Set up output directory
        basePath = Path.Combine(Application.persistentDataPath, "DataCollection");
        if (!Directory.Exists(basePath))
        {
            Directory.CreateDirectory(basePath);
        }

        // Check references
        if (vrCamera == null)
        {
            Debug.LogError("[DataCollector] vrCamera is not assigned!");
            recordData = false;
            return;
        }

        if (ovrCameraRig == null)
        {
            Debug.LogWarning("[DataCollector] ovrCameraRig not assigned. Trying to find one...");
            ovrCameraRig = FindObjectOfType<OVRCameraRig>();
            if (ovrCameraRig == null)
            {
                Debug.LogError("[DataCollector] No OVRCameraRig found in scene!");
                recordData = false;
                return;
            }
        }

        // Start eye tracking via OVRPlugin
        bool eyeTrackingStarted = OVRPlugin.StartEyeTracking();
        Debug.Log($"[DataCollector] Eye tracking started: {eyeTrackingStarted}");

        // Start face tracking via OVRPlugin
        InitializeFaceTracking();

        Debug.Log($"[DataCollector] Participant: {participantID}");
        Debug.Log($"[DataCollector] Data output: {basePath}");
    }

    void Update()
    {
        if (!recordData) return;

        // Start recording on first frame or when new video starts
        if (!isRecording)
        {
            StartNewRecording();
            waitingForVideoStart = true; // don't write data until video plays
        }

        // Wait until the video is actually playing before writing data
        // This prevents stale data from the previous video from being recorded
        if (waitingForVideoStart)
        {
            if (videoPlayer != null && videoPlayer.isPlaying)
            {
                waitingForVideoStart = false;
                Debug.Log("[DataCollector] Video is now playing — data recording active.");
            }
            else
            {
                return; // Skip this frame — video not playing yet
            }
        }

        frameCount++;
        float currentTimestamp = Time.realtimeSinceStartup - sessionStartTime;

        // Calculate delta time for velocities
        float deltaTime = currentTimestamp - prevTimestamp;
        if (deltaTime <= 0f) deltaTime = 0.0111f; // Fallback: ~90Hz

        // =================================================================
        // PLAYBACK TIME SYNC (FIX: read whenever video is prepared,
        // not just when isPlaying — captures first & last frames)
        // =================================================================
        float playbackTime = 0f;
        long videoFrame = -1;
        if (videoPlayer != null && videoPlayer.isPrepared)
        {
            playbackTime = (float)videoPlayer.time;
            videoFrame = videoPlayer.frame;
        }

        // =====================================================================
        // HEAD TRACKING
        // =====================================================================
        Vector3 headPos = vrCamera.position;
        Quaternion headQuat = vrCamera.rotation;
        Vector3 headEuler = vrCamera.eulerAngles;

        // Normalize Euler angles to [-180, 180] range
        float headYaw = NormalizeAngle(headEuler.y);
        float headPitch = NormalizeAngle(headEuler.x);
        float headRoll = NormalizeAngle(headEuler.z);

        // Angular velocity (degrees/second)
        float velYaw = NormalizeAngle(headEuler.y - prevHeadEuler.y) / deltaTime;
        float velPitch = NormalizeAngle(headEuler.x - prevHeadEuler.x) / deltaTime;
        float velRoll = NormalizeAngle(headEuler.z - prevHeadEuler.z) / deltaTime;

        // Write head tracking CSV row
        // FORMAT: Matches Wu_MMSys_17 first 9 columns, then extras
        sb.Clear();
        sb.AppendFormat("{0:F4},{1:F3},{2},", currentTimestamp, playbackTime, videoFrame);
        sb.AppendFormat("{0:F6},{1:F6},{2:F6},{3:F6},", headQuat.x, headQuat.y, headQuat.z, headQuat.w);
        sb.AppendFormat("{0:F6},{1:F6},{2:F6},", headPos.x, headPos.y, headPos.z);
        sb.AppendFormat("{0:F4},{1:F4},{2:F4},", headYaw, headPitch, headRoll);
        sb.AppendFormat("{0:F4},{1:F4},{2:F4}", velYaw, velPitch, velRoll);

        if (headTrackingFile != null)
        {
            headTrackingFile.WriteLine(sb.ToString());
        }

        // =====================================================================
        // FACE TRACKING — Read face data FIRST (needed for eye openness below)
        // =====================================================================
        bool hasFaceData = false;
        float[] faceWeights = null;

        if (faceTrackingAvailable)
        {
            try
            {
                bool gotFaceState = OVRPlugin.GetFaceState(OVRPlugin.Step.Render, 0, ref _faceState);
                if (gotFaceState && _faceState.ExpressionWeights != null &&
                    _faceState.ExpressionWeights.Length > 0)
                {
                    hasFaceData = true;
                    faceWeights = _faceState.ExpressionWeights;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DataCollector] Face tracking read error: {e.Message}");
                faceTrackingAvailable = false;
            }
        }

        // =====================================================================
        // EYE TRACKING
        // =====================================================================
        bool hasEyeData = false;
        Vector3 gazeDir = Vector3.forward;
        Vector3 gazeOrigin = Vector3.zero;
        float leftOpenness = -1f;   // Will be set from face tracking
        float rightOpenness = -1f;  // Will be set from face tracking
        float leftConfidence = 0f;
        float rightConfidence = 0f;
        bool bothEyesValid = false;
        bool blinkL = false;
        bool blinkR = false;

        // ----- Eye openness from face tracking (EyesClosed blend shapes) -----
        // EyesClosed: 0 = fully open, 1 = fully closed → invert for openness
        // When EyeFollowingBlendshapesValid is true, EyesClosed is modified:
        //   true EyesClosed = EyesClosed + min(EyesLookDownL, EyesLookDownR)
        // We apply this correction for accurate blink detection.
        if (hasFaceData && faceWeights != null)
        {
            float rawClosedL = GetFaceWeight(faceWeights, faceIdx_EyeClosedL);
            float rawClosedR = GetFaceWeight(faceWeights, faceIdx_EyeClosedR);
            float lookDownL = GetFaceWeight(faceWeights, faceIdx_EyeLookDownL);
            float lookDownR = GetFaceWeight(faceWeights, faceIdx_EyeLookDownR);

            // Apply EyeFollowingBlendshapes correction:
            // When eye-following mode is active, EyesClosed is reduced by
            // min(LookDownL, LookDownR) to avoid double deformation.
            // We add it back to get the TRUE eyelid closure value.
            float lookDownMin = Mathf.Min(lookDownL, lookDownR);
            float trueClosedL = Mathf.Clamp01(rawClosedL + lookDownMin);
            float trueClosedR = Mathf.Clamp01(rawClosedR + lookDownMin);

            leftOpenness = 1.0f - trueClosedL;
            rightOpenness = 1.0f - trueClosedR;

            // Blink detection: openness below threshold
            blinkL = leftOpenness < blinkThreshold;
            blinkR = rightOpenness < blinkThreshold;
        }

        // ----- Pupil diameter: NOT available on Quest Pro via Meta XR SDK -----
        // The EyeGazeState struct (SDK v85) only has Pose, Confidence, IsValid.
        // No pupil diameter field exists. Values set to -1 to indicate unavailable.
        float leftPupilDiam = -1f;
        float rightPupilDiam = -1f;

        // ----- Eye gaze direction from OVRPlugin -----
        bool gotEyeState = OVRPlugin.GetEyeGazesState(OVRPlugin.Step.Render, -1, ref _eyeGazesState);

        if (gotEyeState)
        {
            OVRPlugin.EyeGazeState leftEye = _eyeGazesState.EyeGazes[0];
            OVRPlugin.EyeGazeState rightEye = _eyeGazesState.EyeGazes[1];

            bool leftValid = leftEye.IsValid;
            bool rightValid = rightEye.IsValid;
            bothEyesValid = leftValid && rightValid;

            if (leftValid || rightValid)
            {
                hasEyeData = true;

                // Convert OVRPlugin pose to Unity vectors
                if (leftValid && rightValid)
                {
                    // Average both eyes for combined gaze
                    Vector3 leftGazeDir = OVRPluginPoseToDirection(leftEye.Pose);
                    Vector3 rightGazeDir = OVRPluginPoseToDirection(rightEye.Pose);
                    gazeDir = ((leftGazeDir + rightGazeDir) * 0.5f).normalized;

                    Vector3 leftOrigin = OVRPluginPoseToPosition(leftEye.Pose);
                    Vector3 rightOrigin = OVRPluginPoseToPosition(rightEye.Pose);
                    gazeOrigin = (leftOrigin + rightOrigin) * 0.5f;

                    leftConfidence = leftEye.Confidence;
                    rightConfidence = rightEye.Confidence;
                }
                else if (leftValid)
                {
                    gazeDir = OVRPluginPoseToDirection(leftEye.Pose);
                    gazeOrigin = OVRPluginPoseToPosition(leftEye.Pose);
                    leftConfidence = leftEye.Confidence;
                }
                else
                {
                    gazeDir = OVRPluginPoseToDirection(rightEye.Pose);
                    gazeOrigin = OVRPluginPoseToPosition(rightEye.Pose);
                    rightConfidence = rightEye.Confidence;
                }

                // If face tracking didn't provide openness, use confidence as
                // last-resort fallback (less accurate but better than nothing)
                if (leftOpenness < 0f)
                {
                    leftOpenness = leftValid ? 1f : 0f;
                    rightOpenness = rightValid ? 1f : 0f;
                    blinkL = false;
                    blinkR = false;
                    Debug.LogWarning("[DataCollector] Face tracking unavailable for eye openness — using fallback.");
                }

                // Transform gaze direction to world space
                Transform trackingSpace = ovrCameraRig.trackingSpace;
                if (trackingSpace != null)
                {
                    gazeDir = trackingSpace.TransformDirection(gazeDir);
                    gazeOrigin = trackingSpace.TransformPoint(gazeOrigin);
                }
            }
        }

        // If no eye data AND no face data for openness, set openness to -1
        if (leftOpenness < 0f)
        {
            leftOpenness = -1f;
            rightOpenness = -1f;
        }

        // Additional lid metrics from face tracking
        float lidTightL = GetFaceWeight(faceWeights, faceIdx_LidTightenerL);
        float lidTightR = GetFaceWeight(faceWeights, faceIdx_LidTightenerR);
        float upperLidL = GetFaceWeight(faceWeights, faceIdx_UpperLidRaiserL);
        float upperLidR = GetFaceWeight(faceWeights, faceIdx_UpperLidRaiserR);

        // Convert gaze to spherical coordinates (yaw/pitch)
        float gazeYaw = 0f;
        float gazePitch = 0f;
        if (hasEyeData)
        {
            gazeYaw = Mathf.Atan2(gazeDir.x, gazeDir.z) * Mathf.Rad2Deg;
            gazePitch = Mathf.Asin(Mathf.Clamp(gazeDir.y, -1f, 1f)) * Mathf.Rad2Deg;
        }

        // Fixation detection (I-VT: velocity threshold)
        bool isFixating = false;
        float fixationDuration = 0f;
        if (hasEyeData)
        {
            float gazeAngularVelocity = Vector3.Angle(gazeDir, prevGazeDir) / deltaTime;
            isFixating = gazeAngularVelocity < fixationThreshold;

            if (isFixating && wasFixating)
            {
                fixationDuration = (currentTimestamp - fixationStartTime) * 1000f; // ms
            }
            else if (isFixating && !wasFixating)
            {
                fixationStartTime = currentTimestamp;
                fixationDuration = 0f;
            }
            wasFixating = isFixating;
        }

        // Write eye tracking CSV row
        sb.Clear();
        sb.AppendFormat("{0:F4},{1:F3},{2},", currentTimestamp, playbackTime, videoFrame);
        sb.AppendFormat("{0:F6},{1:F6},{2:F6},", gazeDir.x, gazeDir.y, gazeDir.z);
        sb.AppendFormat("{0:F6},{1:F6},{2:F6},", gazeOrigin.x, gazeOrigin.y, gazeOrigin.z);
        sb.AppendFormat("{0:F4},{1:F4},", gazeYaw, gazePitch);
        sb.AppendFormat("{0:F4},{1:F4},", leftPupilDiam, rightPupilDiam);
        sb.AppendFormat("{0:F4},{1:F4},", leftOpenness, rightOpenness);
        sb.AppendFormat("{0},{1},", blinkL ? 1 : 0, blinkR ? 1 : 0);
        sb.AppendFormat("{0:F4},{1:F4},", lidTightL, lidTightR);
        sb.AppendFormat("{0:F4},{1:F4},", upperLidL, upperLidR);
        sb.AppendFormat("{0:F4},{1:F4},", leftConfidence, rightConfidence);
        sb.AppendFormat("{0},{1:F1},{2}", isFixating ? 1 : 0, fixationDuration, bothEyesValid ? 1 : 0);

        if (eyeTrackingFile != null)
        {
            eyeTrackingFile.WriteLine(sb.ToString());
        }

        // =====================================================================
        // FACE TRACKING — Write CSV row (data was read above)
        // =====================================================================
        if (faceTrackingFile != null)
        {
            sb.Clear();
            sb.AppendFormat("{0:F4},{1:F3},{2},", currentTimestamp, playbackTime, videoFrame);
            sb.Append(hasFaceData ? "1," : "0,");

            for (int i = 0; i < faceExpressionCount; i++)
            {
                float w = (faceWeights != null && i < faceWeights.Length) ? faceWeights[i] : 0f;
                sb.AppendFormat("{0:F4}", w);
                if (i < faceExpressionCount - 1) sb.Append(',');
            }

            faceTrackingFile.WriteLine(sb.ToString());
        }

        // =====================================================================
        // COMBINED DATA
        // =====================================================================
        float gazeRelativeH = hasEyeData ? NormalizeAngle(gazeYaw - headYaw) : 0f;
        float gazeRelativeV = hasEyeData ? NormalizeAngle(gazePitch - headPitch) : 0f;
        float eyeHeadOffset = Mathf.Sqrt(gazeRelativeH * gazeRelativeH + gazeRelativeV * gazeRelativeV);

        // Absolute gaze in world (for viewport prediction)
        float absoluteGazeYaw = hasEyeData ? gazeYaw : headYaw;
        float absoluteGazePitch = hasEyeData ? gazePitch : headPitch;

        // Face summary metrics for combined CSV
        float smileL = GetFaceWeight(faceWeights, faceIdx_SmileL);
        float smileR = GetFaceWeight(faceWeights, faceIdx_SmileR);
        float browDownL = GetFaceWeight(faceWeights, faceIdx_BrowDownL);
        float browDownR = GetFaceWeight(faceWeights, faceIdx_BrowDownR);
        float jawOpen = GetFaceWeight(faceWeights, faceIdx_JawDrop);
        float eyeClosedL = GetFaceWeight(faceWeights, faceIdx_EyeClosedL);
        float eyeClosedR = GetFaceWeight(faceWeights, faceIdx_EyeClosedR);

        // Face activity: average of all expression weights (0 = neutral, higher = more expression)
        float faceActivity = 0f;
        if (hasFaceData && faceWeights != null && faceWeights.Length > 0)
        {
            float sum = 0f;
            for (int i = 0; i < faceWeights.Length; i++)
                sum += faceWeights[i];
            faceActivity = sum / faceWeights.Length;
        }

        sb.Clear();
        sb.AppendFormat("{0:F4},{1:F3},{2},", currentTimestamp, playbackTime, videoFrame);
        sb.AppendFormat("{0:F4},{1:F4},{2:F4},", headYaw, headPitch, headRoll);
        sb.AppendFormat("{0:F4},{1:F4},", gazeYaw, gazePitch);
        sb.AppendFormat("{0:F4},{1:F4},", gazeRelativeH, gazeRelativeV);
        sb.AppendFormat("{0:F4},", eyeHeadOffset);
        sb.AppendFormat("{0:F4},{1:F4},", absoluteGazeYaw, absoluteGazePitch);
        // Eye openness + blinks
        sb.AppendFormat("{0:F4},{1:F4},", leftOpenness, rightOpenness);
        sb.AppendFormat("{0},{1},", blinkL ? 1 : 0, blinkR ? 1 : 0);
        // Face columns
        sb.AppendFormat("{0},", hasFaceData ? 1 : 0);
        sb.AppendFormat("{0:F3},{1:F3},", smileL, smileR);
        sb.AppendFormat("{0:F3},{1:F3},", browDownL, browDownR);
        sb.AppendFormat("{0:F3},", jawOpen);
        sb.AppendFormat("{0:F3},{1:F3},", eyeClosedL, eyeClosedR);
        sb.AppendFormat("{0:F4}", faceActivity);

        if (combinedFile != null)
        {
            combinedFile.WriteLine(sb.ToString());
        }

        // =====================================================================
        // UPDATE PREVIOUS FRAME STATE
        // =====================================================================
        prevHeadEuler = headEuler;
        prevGazeDir = gazeDir;
        prevTimestamp = currentTimestamp;

        // Periodic flush to prevent data loss on crash
        if (frameCount % flushInterval == 0)
        {
            FlushAllFiles();
        }

        // NOTE: Video-end detection is handled by VideoManager (loopPointReached
        // callback + backup check in VideoManager.Update). DataCollector does NOT
        // check for video end — doing so causes a race condition when switching
        // videos because the new video is "not playing" while it prepares, and
        // stale time from the old video triggers an immediate StopRecording().
    }

    // =========================================================================
    // FACE TRACKING INITIALIZATION
    // =========================================================================

    /// <summary>
    /// Initializes face tracking via OVRPlugin.
    /// Caches expression names and key indices for the combined CSV.
    /// Requires OVRManager.FaceTrackingSupport set to Supported/Required.
    /// </summary>
    private void InitializeFaceTracking()
    {
        try
        {
            bool started = OVRPlugin.StartFaceTracking();
            Debug.Log($"[DataCollector] Face tracking started: {started}");

            if (started)
            {
                faceTrackingAvailable = true;
                faceExpressionCount = (int)OVRPlugin.FaceExpression.Max;

                // Cache expression names for CSV header
                faceExpressionNames = new string[faceExpressionCount];
                for (int i = 0; i < faceExpressionCount; i++)
                {
                    faceExpressionNames[i] = ((OVRPlugin.FaceExpression)i).ToString();
                }

                // Cache key expression indices for combined CSV summary
                // NOTE: OVRPlugin.FaceExpression uses underscore naming (e.g. Lip_Corner_Puller_L)
                CacheFaceExpressionIndex("Lip_Corner_Puller_L", ref faceIdx_SmileL);
                CacheFaceExpressionIndex("Lip_Corner_Puller_R", ref faceIdx_SmileR);
                CacheFaceExpressionIndex("Brow_Lowerer_L", ref faceIdx_BrowDownL);
                CacheFaceExpressionIndex("Brow_Lowerer_R", ref faceIdx_BrowDownR);
                CacheFaceExpressionIndex("Jaw_Drop", ref faceIdx_JawDrop);

                // Eye openness / blink expressions
                CacheFaceExpressionIndex("Eyes_Closed_L", ref faceIdx_EyeClosedL);
                CacheFaceExpressionIndex("Eyes_Closed_R", ref faceIdx_EyeClosedR);
                CacheFaceExpressionIndex("Eyes_Look_Down_L", ref faceIdx_EyeLookDownL);
                CacheFaceExpressionIndex("Eyes_Look_Down_R", ref faceIdx_EyeLookDownR);

                // Lid expressions for comprehensive blink analysis
                CacheFaceExpressionIndex("Lid_Tightener_L", ref faceIdx_LidTightenerL);
                CacheFaceExpressionIndex("Lid_Tightener_R", ref faceIdx_LidTightenerR);
                CacheFaceExpressionIndex("Upper_Lid_Raiser_L", ref faceIdx_UpperLidRaiserL);
                CacheFaceExpressionIndex("Upper_Lid_Raiser_R", ref faceIdx_UpperLidRaiserR);

                Debug.Log($"[DataCollector] Face tracking: {faceExpressionCount} expressions available.");
                Debug.Log($"[DataCollector] EyeClosedL idx={faceIdx_EyeClosedL}, EyeClosedR idx={faceIdx_EyeClosedR}");
                Debug.Log($"[DataCollector] SmileL idx={faceIdx_SmileL}, JawDrop idx={faceIdx_JawDrop}");
            }
            else
            {
                faceTrackingAvailable = false;
                Debug.LogWarning("[DataCollector] Face tracking failed to start. " +
                    "Ensure OVRManager has Face Tracking Support = Supported/Required.");
            }
        }
        catch (Exception e)
        {
            faceTrackingAvailable = false;
            Debug.LogWarning($"[DataCollector] Face tracking init error (SDK may not support it): {e.Message}");
        }
    }

    /// <summary>
    /// Tries to resolve a face expression enum name to its integer index.
    /// </summary>
    private void CacheFaceExpressionIndex(string expressionName, ref int target)
    {
        try
        {
            if (Enum.TryParse<OVRPlugin.FaceExpression>(expressionName, out var expr))
            {
                target = (int)expr;
            }
            else
            {
                target = -1;
                Debug.LogWarning($"[DataCollector] Face expression not found: {expressionName}");
            }
        }
        catch
        {
            target = -1;
        }
    }

    /// <summary>
    /// Safely read a face expression weight by cached index.
    /// Returns 0 if index is invalid or weights are null.
    /// </summary>
    private float GetFaceWeight(float[] weights, int index)
    {
        if (weights == null || index < 0 || index >= weights.Length) return 0f;
        return weights[index];
    }

    // =========================================================================
    // PUBLIC METHODS (called by VideoManager)
    // =========================================================================

    /// <summary>
    /// Call this when switching to a new video.
    /// Closes current files and prepares for new recording.
    /// </summary>
    public void PrepareForNewVideo(string newVideoID)
    {
        // Close current recording if active
        if (isRecording)
        {
            StopRecording();
        }

        videoID = newVideoID;
        Debug.Log($"[DataCollector] Prepared for video: {videoID}");
    }

    /// <summary>
    /// Starts recording. Called automatically on first Update frame.
    /// </summary>
    public void StartNewRecording()
    {
        if (isRecording) return;

        // Refuse to record if videoID is empty (no video selected yet)
        if (string.IsNullOrEmpty(videoID))
        {
            Debug.LogWarning("[DataCollector] StartNewRecording skipped — no videoID set.");
            recordData = false;
            return;
        }

        string dateStamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

        // FOLDER STRUCTURE:
        //   DataCollection / {participantID} / {videoID} / head.csv, eye.csv, face.csv, combined.csv
        // Example:
        //   DataCollection / P345 / video_01_Weekly_Idol-Dancing / head.csv
        string participantPath = Path.Combine(basePath, participantID);
        string videoPath = Path.Combine(participantPath, videoID);
        if (!Directory.Exists(videoPath))
        {
            Directory.CreateDirectory(videoPath);
        }

        // ---- Head tracking file ----
        string headPath = Path.Combine(videoPath, "head.csv");
        headTrackingFile = new StreamWriter(headPath, false, Encoding.UTF8);
        headTrackingFile.WriteLine(
            "Timestamp,PlaybackTime,VideoFrame," +
            "UnitQuaternion.x,UnitQuaternion.y,UnitQuaternion.z,UnitQuaternion.w," +
            "HmdPosition.x,HmdPosition.y,HmdPosition.z," +
            "EulerYaw,EulerPitch,EulerRoll," +
            "VelYaw,VelPitch,VelRoll");

        // ---- Eye tracking file ----
        string eyePath = Path.Combine(videoPath, "eye.csv");
        eyeTrackingFile = new StreamWriter(eyePath, false, Encoding.UTF8);
        eyeTrackingFile.WriteLine(
            "Timestamp,PlaybackTime,VideoFrame," +
            "GazeDir.x,GazeDir.y,GazeDir.z," +
            "GazeOrigin.x,GazeOrigin.y,GazeOrigin.z," +
            "GazeYaw,GazePitch," +
            "LeftPupilDiam_NA,RightPupilDiam_NA," +
            "LeftOpenness,RightOpenness," +
            "BlinkL,BlinkR," +
            "LidTightenerL,LidTightenerR," +
            "UpperLidRaiserL,UpperLidRaiserR," +
            "LeftConfidence,RightConfidence," +
            "IsFixating,FixationDurationMs,BothEyesValid");

        // ---- Face tracking file ----
        string facePath = Path.Combine(videoPath, "face.csv");
        faceTrackingFile = new StreamWriter(facePath, false, Encoding.UTF8);
        {
            StringBuilder faceHeader = new StringBuilder();
            faceHeader.Append("Timestamp,PlaybackTime,VideoFrame,FaceIsValid");
            if (faceExpressionNames != null)
            {
                for (int i = 0; i < faceExpressionNames.Length; i++)
                {
                    faceHeader.Append(',');
                    faceHeader.Append(faceExpressionNames[i]);
                }
            }
            else
            {
                // Fallback: generic column names
                int count = faceExpressionCount > 0 ? faceExpressionCount : 63;
                for (int i = 0; i < count; i++)
                {
                    faceHeader.AppendFormat(",Expr_{0}", i);
                }
            }
            faceTrackingFile.WriteLine(faceHeader.ToString());
        }

        // ---- Combined file ----
        string combinedPath = Path.Combine(videoPath, "combined.csv");
        combinedFile = new StreamWriter(combinedPath, false, Encoding.UTF8);
        combinedFile.WriteLine(
            "Timestamp,PlaybackTime,VideoFrame," +
            "HeadYaw,HeadPitch,HeadRoll," +
            "GazeYaw,GazePitch," +
            "GazeRelativeH,GazeRelativeV," +
            "EyeHeadOffset," +
            "AbsoluteGazeYaw,AbsoluteGazePitch," +
            "LeftOpenness,RightOpenness," +
            "BlinkL,BlinkR," +
            "FaceIsValid,SmileL,SmileR,BrowDownL,BrowDownR," +
            "JawOpen,EyeClosedL,EyeClosedR,FaceActivity");

        // Initialize state
        sessionStartTime = Time.realtimeSinceStartup;
        frameCount = 0;
        prevTimestamp = 0f;
        prevHeadEuler = vrCamera.eulerAngles;
        prevGazeDir = Vector3.forward;
        fixationStartTime = 0f;
        wasFixating = false;
        isRecording = true;
        filesOpen = true;

        Debug.Log($"[DataCollector] Recording started for {participantID}/{videoID}");
        Debug.Log($"[DataCollector] Files at: {videoPath}");
        Debug.Log($"[DataCollector] Files: head.csv, eye.csv, face.csv, combined.csv");
    }

    /// <summary>
    /// Stops recording and closes all files safely.
    /// </summary>
    public void StopRecording()
    {
        if (!filesOpen) return;

        isRecording = false;
        recordData = false;

        FlushAllFiles();
        CloseAllFiles();

        Debug.Log($"[DataCollector] Recording stopped. {frameCount} frames saved for {participantID}/{videoID}");
        Debug.Log($"[DataCollector] Files at: {Path.Combine(basePath, participantID)}");
    }

    // =========================================================================
    // HELPER METHODS
    // =========================================================================

    /// <summary>
    /// Normalize angle to [-180, +180] range.
    /// </summary>
    private float NormalizeAngle(float angle)
    {
        while (angle > 180f) angle -= 360f;
        while (angle < -180f) angle += 360f;
        return angle;
    }

    /// <summary>
    /// Convert OVRPlugin.Posef orientation to a forward direction vector.
    /// OVRPlugin uses a right-handed coordinate system; Unity is left-handed.
    /// The gaze direction is the forward vector of the pose rotation.
    /// </summary>
    private Vector3 OVRPluginPoseToDirection(OVRPlugin.Posef pose)
    {
        // Convert OVRPlugin quaternion to Unity quaternion
        // OVRPlugin: right-handed (x-right, y-up, z-backward)
        // Unity: left-handed (x-right, y-up, z-forward)
        Quaternion rotation = new Quaternion(
            pose.Orientation.x,
            pose.Orientation.y,
            -pose.Orientation.z,
            -pose.Orientation.w
        );

        // The gaze direction is the forward vector of this rotation
        return rotation * Vector3.forward;
    }

    /// <summary>
    /// Convert OVRPlugin.Posef position to Unity Vector3.
    /// </summary>
    private Vector3 OVRPluginPoseToPosition(OVRPlugin.Posef pose)
    {
        // Flip Z axis for Unity's left-handed coordinate system
        return new Vector3(
            pose.Position.x,
            pose.Position.y,
            -pose.Position.z
        );
    }

    private void FlushAllFiles()
    {
        try
        {
            headTrackingFile?.Flush();
            eyeTrackingFile?.Flush();
            faceTrackingFile?.Flush();
            combinedFile?.Flush();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[DataCollector] Flush error: {e.Message}");
        }
    }

    private void CloseAllFiles()
    {
        try
        {
            if (headTrackingFile != null) { headTrackingFile.Close(); headTrackingFile = null; }
            if (eyeTrackingFile != null) { eyeTrackingFile.Close(); eyeTrackingFile = null; }
            if (faceTrackingFile != null) { faceTrackingFile.Close(); faceTrackingFile = null; }
            if (combinedFile != null) { combinedFile.Close(); combinedFile = null; }
            filesOpen = false;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[DataCollector] Close error: {e.Message}");
        }
    }

    // =========================================================================
    // UNITY CALLBACKS — Ensure data is saved even on unexpected exit
    // =========================================================================

    void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus && isRecording)
        {
            FlushAllFiles();
            Debug.Log("[DataCollector] App paused — data flushed.");
        }
    }

    void OnApplicationQuit()
    {
        StopRecording();
    }

    void OnDestroy()
    {
        StopRecording();
    }
}
