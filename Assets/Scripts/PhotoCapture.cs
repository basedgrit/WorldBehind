using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System;

// Example setup to work with PhotoJournal:
// 1. Add a PhotoCapture component to your camera GameObject
// 2. Create a new UI GameObject with both PhotoJournal and several RawImage components
// 3. Connect them in a script or the Inspector like this:
//    PhotoJournal journal = FindObjectOfType<PhotoJournal>();
//    PhotoCapture camera = FindObjectOfType<PhotoCapture>();
//    journal.AddPhoto(camera.GetLastPhoto());  // Call this after taking a photo
//
// See PhotoJournal.cs for more details on setting up the journal slots.

public class PhotoCapture : MonoBehaviour
{
    public event Action<Texture2D> OnPhotoCaptured;
    public event Action<Texture2D, string> OnPhotoCapturedDetailed;

    // The RenderTexture that the camera draws into. Think of this as the "screen" we copy from.
    public RenderTexture photoRenderTexture;

    // The UI image that shows the captured photo. Drag a RawImage here in the Inspector.
    public RawImage photoPreview;

    // Optional: a Material to also show the photo on (for example, on a 3D object).
    // If you don't set this, the code will still update the UI preview.
    public Material photoMaterial;

    // Camera used for zoom + capture calculations. If left empty, Camera.main is used.
    public Camera captureCamera;
    [Tooltip("Optional reference to FirstPersonController to auto-sync zoom detection values.")]
    public FirstPersonController firstPersonController;
    [Tooltip("Use FirstPersonController.playerCamera as the capture source when available.")]
    public bool useFirstPersonCamera = true;
    [Tooltip("Prefer capturing from the live camera view instead of an assigned RenderTexture.")]
    public bool preferLiveCameraCapture = true;

    [Header("Zoom Detection")]
    [Tooltip("The non-zoom FOV used only for detecting whether zoom is active.")]
    public float normalFov = 60f;
    [Tooltip("The zoomed-in FOV used only for detecting whether zoom is active.")]
    public float zoomFov = 30f;
    [Tooltip("If false, the overlay is always visible instead of only while zoomed.")]
    public bool showOverlayOnlyWhenZoomed = true;

    [Header("Overlay UI")]
    [Tooltip("Crosshair shown only while zoomed in.")]
    public GameObject crosshairUI;
    [Tooltip("Square capture frame shown only while zoomed in.")]
    public RectTransform captureSquareUI;
    [Tooltip("Canvas camera if your canvas is Screen Space - Camera / World Space.")]
    public Camera canvasCamera;

    [Header("Capture")]
    [Tooltip("If no square UI is assigned, this centered square size is used (0-1 of screen min dimension).")]
    [Range(0.1f, 1f)]
    public float fallbackSquareSizePercent = 0.5f;
    [Tooltip("Optional: prevent photos unless zoomed in.")]
    public bool captureOnlyWhenZoomed = false;
    [Tooltip("Optional: auto-add each captured photo to PhotoJournal.")]
    public bool addPhotoToJournal = true;
    public PhotoJournal photoJournal;
    [Tooltip("Capture directly from the final screen image. This best matches what the player is aiming at.")]
    public bool captureFromScreenBuffer = true;
    [Tooltip("Temporarily hide the zoom crosshair while taking the photo so it is not captured.")]
    public bool hideCrosshairDuringCapture = true;
    [Tooltip("Resolution scale used when capturing directly from camera and no RenderTexture is assigned.")]
    [Range(0.5f, 2f)]
    public float fallbackCaptureScale = 1f;

    [Header("Subject Detection")]
    [Tooltip("Try to detect what was photographed by raycasting from the center of the capture area.")]
    public bool enableSubjectDetection = true;
    [Tooltip("Only these layers are considered when detecting the photographed object.")]
    public LayerMask detectionLayers = ~0;
    public float detectionMaxDistance = 200f;
    [Tooltip("If true, only objects with PhotoSubject component can be detected.")]
    public bool requirePhotoSubjectComponent = false;

    [Header("Flash")]
    [Tooltip("Show a brief screen flash after each captured photo.")]
    public bool enableScreenFlash = true;
    [Tooltip("Fullscreen UI image used for the flash effect.")]
    public Image flashOverlay;
    public Color flashColor = new Color(1f, 1f, 1f, 0.8f);
    [Range(0.001f, 0.2f)]
    public float flashInDuration = 0.02f;
    [Range(0.01f, 0.5f)]
    public float flashOutDuration = 0.12f;
    public bool flashUseUnscaledTime = true;

    [Header("Corner Preview")]
    [Tooltip("Show a brief corner pop-up preview of the captured photo.")]
    public bool enableCornerPreview = true;
    [Tooltip("RawImage used for the corner preview animation.")]
    public RawImage cornerPreviewImage;
    [Tooltip("Optional CanvasGroup for corner preview alpha. Auto-added if missing.")]
    public CanvasGroup cornerPreviewCanvasGroup;
    [Tooltip("Optional RectTransform target for corner preview. Uses image RectTransform if empty.")]
    public RectTransform cornerPreviewRect;
    [Tooltip("Where the preview starts/ends relative to shown anchored position.")]
    public Vector2 cornerPreviewHiddenOffset = new Vector2(180f, -60f);
    [Range(0.5f, 1f)]
    public float cornerPreviewStartScale = 0.9f;
    [Range(1f, 1.4f)]
    public float cornerPreviewPopScale = 1.08f;
    [Range(0.05f, 0.6f)]
    public float cornerPreviewInDuration = 0.14f;
    [Range(0f, 2f)]
    public float cornerPreviewHoldDuration = 0.7f;
    [Range(0.05f, 0.8f)]
    public float cornerPreviewOutDuration = 0.2f;
    public bool cornerPreviewUseUnscaledTime = true;

    [Header("Corner Preview Polaroid")]
    [Tooltip("Apply a polaroid-like tilt and settle animation.")]
    public bool cornerPreviewUsePolaroidStyle = true;
    [Tooltip("Add slight random tilt each time for a natural snap look.")]
    public bool cornerPreviewUseRandomTilt = true;
    [Range(-20f, 20f)]
    public float cornerPreviewBaseTilt = -6f;
    [Range(0f, 15f)]
    public float cornerPreviewRandomTiltRange = 4f;
    [Range(0f, 20f)]
    public float cornerPreviewPopTilt = 8f;
    [Tooltip("Add drop shadow to the corner preview image.")]
    public bool cornerPreviewAddDropShadow = true;
    public Color cornerPreviewShadowColor = new Color(0f, 0f, 0f, 0.35f);
    public Vector2 cornerPreviewShadowOffset = new Vector2(6f, -6f);

    // Stores the last photo we grabbed. It's null until you take one.
    private Texture2D capturedPhoto;
    private bool isZoomed;
    private readonly Vector3[] uiCorners = new Vector3[4];
    private Coroutine flashRoutine;
    private Coroutine cornerPreviewRoutine;
    private string lastDetectedSubjectName = "";
    private Vector2 cornerPreviewShownPosition;
    private Vector3 cornerPreviewShownScale = Vector3.one;
    private float cornerPreviewShownRotationZ;
    private float cornerPreviewTargetTilt;

    void Awake()
    {
        if (captureCamera == null)
        {
            captureCamera = GetComponent<Camera>();
        }

        if (firstPersonController == null)
        {
            firstPersonController = FindFirstObjectByType<FirstPersonController>();
        }

        if (firstPersonController != null && firstPersonController.playerCamera != null && useFirstPersonCamera)
        {
            captureCamera = firstPersonController.playerCamera;
        }
        else if (captureCamera == null && firstPersonController != null)
        {
            captureCamera = firstPersonController.playerCamera;
        }

        if (captureCamera == null)
        {
            captureCamera = Camera.main;
        }

        if (photoJournal == null)
        {
            photoJournal = FindFirstObjectByType<PhotoJournal>();
        }

        InitializeFlashOverlay();
        InitializeCornerPreview();
        SetOverlayVisible(false);
    }

    // Check for a mouse click each frame and take a picture when clicked.
    void Update()
    {
        UpdateZoomState();

        // Left-click to take a picture.
        if (Input.GetMouseButtonDown(0))
        {
            if (captureOnlyWhenZoomed && !isZoomed)
            {
                return;
            }

            Debug.Log("Image Taken");
            StartCoroutine(CapturePhoto());
        }
    }

    private void UpdateZoomState()
    {
        if (captureCamera == null)
        {
            isZoomed = false;
            SetOverlayVisible(false);
            return;
        }

        float detectedNormalFov = firstPersonController != null ? firstPersonController.fov : normalFov;
        float detectedZoomFov = firstPersonController != null ? firstPersonController.zoomFOV : zoomFov;
        float currentFov = captureCamera.fieldOfView;

        // Compare distance to zoom and normal FOV so visibility stays stable
        // even if configured values shift slightly.
        float distanceToZoom = Mathf.Abs(currentFov - detectedZoomFov);
        float distanceToNormal = Mathf.Abs(currentFov - detectedNormalFov);
        isZoomed = distanceToZoom <= distanceToNormal + 0.25f;
        SetOverlayVisible(showOverlayOnlyWhenZoomed ? isZoomed : true);
    }

    private void SetOverlayVisible(bool visible)
    {
        if (crosshairUI != null)
        {
            crosshairUI.SetActive(visible);
        }

        if (captureSquareUI != null)
        {
            captureSquareUI.gameObject.SetActive(visible);
        }
    }

    // Take a snapshot of the RenderTexture. We wait until the frame finishes
    // rendering so the image is complete, then copy pixels into a Texture2D.
    IEnumerator CapturePhoto()
    {
        bool crosshairWasVisible = crosshairUI != null && crosshairUI.activeSelf;
        if (hideCrosshairDuringCapture && crosshairWasVisible)
        {
            crosshairUI.SetActive(false);
        }

        if (captureCamera == null && photoRenderTexture == null)
        {
            Debug.LogError("No capture source found. Assign captureCamera or photoRenderTexture.");
            RestoreOverlayAfterCapture(crosshairWasVisible);
            yield break;
        }

        // Wait until the current frame is done so we capture the final image.
        yield return new WaitForEndOfFrame();

        if (captureFromScreenBuffer)
        {
            RectInt screenCaptureRect = GetCaptureRectOnScreenPixels();
            capturedPhoto = new Texture2D(screenCaptureRect.width, screenCaptureRect.height, TextureFormat.RGB24, false);
            capturedPhoto.ReadPixels(
                new Rect(screenCaptureRect.x, screenCaptureRect.y, screenCaptureRect.width, screenCaptureRect.height),
                0,
                0
            );
            capturedPhoto.Apply();
            UpdateDetectedSubject();
            PublishCapturedPhoto();
            RestoreOverlayAfterCapture(crosshairWasVisible);
            yield break;
        }

        bool useLiveCameraSource = captureCamera != null && (preferLiveCameraCapture || photoRenderTexture == null);

        RenderTexture sourceRT = useLiveCameraSource ? null : photoRenderTexture;
        RenderTexture temporaryRT = null;
        Rect referenceScreenRect = new Rect(0f, 0f, Screen.width, Screen.height);

        if (useLiveCameraSource)
        {
            referenceScreenRect = captureCamera.pixelRect;
            int width = Mathf.Max(1, Mathf.RoundToInt(referenceScreenRect.width * fallbackCaptureScale));
            int height = Mathf.Max(1, Mathf.RoundToInt(referenceScreenRect.height * fallbackCaptureScale));
            temporaryRT = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);

            RenderTexture previousTarget = captureCamera.targetTexture;
            captureCamera.targetTexture = temporaryRT;
            captureCamera.Render();
            captureCamera.targetTexture = previousTarget;

            sourceRT = temporaryRT;
        }
        else if (sourceRT == null)
        {
            Debug.LogError("Photo capture source is null.");
            RestoreOverlayAfterCapture(crosshairWasVisible);
            yield break;
        }

        // Remember which render target was active so we can put it back later.
        RenderTexture currentRT = RenderTexture.active;

        // Make the source RT active so ReadPixels reads from it.
        RenderTexture.active = sourceRT;

        RectInt captureRect = GetCaptureRectOnRenderTexture(sourceRT.width, sourceRT.height, referenceScreenRect);

        // Make a new texture sized to our square capture area.
        capturedPhoto = new Texture2D(captureRect.width, captureRect.height, TextureFormat.RGB24, false);

        // Copy only the square capture area from the active RenderTexture.
        capturedPhoto.ReadPixels(new Rect(captureRect.x, captureRect.y, captureRect.width, captureRect.height), 0, 0);
        capturedPhoto.Apply();
        UpdateDetectedSubject();

        // Put the previously active render target back so we don't break other rendering.
        RenderTexture.active = currentRT;

        if (temporaryRT != null)
        {
            RenderTexture.ReleaseTemporary(temporaryRT);
        }

        PublishCapturedPhoto();
        RestoreOverlayAfterCapture(crosshairWasVisible);
    }

    private RectInt GetCaptureRectOnRenderTexture(int targetWidth, int targetHeight, Rect referenceScreenRect)
    {
        Rect screenRect = GetCaptureRectOnScreen();

        float refWidth = Mathf.Max(1f, referenceScreenRect.width);
        float refHeight = Mathf.Max(1f, referenceScreenRect.height);

        float normalizedXMin = Mathf.Clamp01((screenRect.xMin - referenceScreenRect.xMin) / refWidth);
        float normalizedYMin = Mathf.Clamp01((screenRect.yMin - referenceScreenRect.yMin) / refHeight);
        float normalizedXMax = Mathf.Clamp01((screenRect.xMax - referenceScreenRect.xMin) / refWidth);
        float normalizedYMax = Mathf.Clamp01((screenRect.yMax - referenceScreenRect.yMin) / refHeight);

        int x = Mathf.RoundToInt(normalizedXMin * targetWidth);
        int y = Mathf.RoundToInt(normalizedYMin * targetHeight);
        int width = Mathf.RoundToInt(Mathf.Max(0f, normalizedXMax - normalizedXMin) * targetWidth);
        int height = Mathf.RoundToInt(Mathf.Max(0f, normalizedYMax - normalizedYMin) * targetHeight);

        int squareSize = Mathf.Min(width, height);
        int squareX = x + (width - squareSize) / 2;
        int squareY = y + (height - squareSize) / 2;

        squareX = Mathf.Clamp(squareX, 0, Mathf.Max(0, targetWidth - 1));
        squareY = Mathf.Clamp(squareY, 0, Mathf.Max(0, targetHeight - 1));
        squareSize = Mathf.Clamp(squareSize, 1, Mathf.Min(targetWidth - squareX, targetHeight - squareY));

        return new RectInt(squareX, squareY, squareSize, squareSize);
    }

    private Rect GetCaptureRectOnScreen()
    {
        if (captureSquareUI == null)
        {
            float size = Mathf.Min(Screen.width, Screen.height) * fallbackSquareSizePercent;
            float fallbackX = (Screen.width - size) * 0.5f;
            float fallbackY = (Screen.height - size) * 0.5f;
            return new Rect(fallbackX, fallbackY, size, size);
        }

        captureSquareUI.GetWorldCorners(uiCorners);

        Camera overlayCamera = canvasCamera;
        if (overlayCamera == null)
        {
            Canvas parentCanvas = captureSquareUI.GetComponentInParent<Canvas>();
            if (parentCanvas != null && parentCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
            {
                overlayCamera = parentCanvas.worldCamera;
            }
        }

        Vector2 p0 = RectTransformUtility.WorldToScreenPoint(overlayCamera, uiCorners[0]);
        Vector2 p1 = RectTransformUtility.WorldToScreenPoint(overlayCamera, uiCorners[1]);
        Vector2 p2 = RectTransformUtility.WorldToScreenPoint(overlayCamera, uiCorners[2]);
        Vector2 p3 = RectTransformUtility.WorldToScreenPoint(overlayCamera, uiCorners[3]);

        float xMin = Mathf.Min(p0.x, p1.x, p2.x, p3.x);
        float xMax = Mathf.Max(p0.x, p1.x, p2.x, p3.x);
        float yMin = Mathf.Min(p0.y, p1.y, p2.y, p3.y);
        float yMax = Mathf.Max(p0.y, p1.y, p2.y, p3.y);

        float width = Mathf.Max(1f, xMax - xMin);
        float height = Mathf.Max(1f, yMax - yMin);
        float squareSize = Mathf.Min(width, height);

        float centerX = (xMin + xMax) * 0.5f;
        float centerY = (yMin + yMax) * 0.5f;
        float x = centerX - (squareSize * 0.5f);
        float y = centerY - (squareSize * 0.5f);

        x = Mathf.Clamp(x, 0f, Mathf.Max(0f, Screen.width - squareSize));
        y = Mathf.Clamp(y, 0f, Mathf.Max(0f, Screen.height - squareSize));

        return new Rect(x, y, squareSize, squareSize);
    }

    private RectInt GetCaptureRectOnScreenPixels()
    {
        Rect screenRect = GetCaptureRectOnScreen();

        int x = Mathf.RoundToInt(screenRect.x);
        int y = Mathf.RoundToInt(screenRect.y);
        int width = Mathf.RoundToInt(screenRect.width);
        int height = Mathf.RoundToInt(screenRect.height);

        int squareSize = Mathf.Max(1, Mathf.Min(width, height));
        int squareX = x + (width - squareSize) / 2;
        int squareY = y + (height - squareSize) / 2;

        squareX = Mathf.Clamp(squareX, 0, Mathf.Max(0, Screen.width - 1));
        squareY = Mathf.Clamp(squareY, 0, Mathf.Max(0, Screen.height - 1));
        squareSize = Mathf.Clamp(squareSize, 1, Mathf.Min(Screen.width - squareX, Screen.height - squareY));

        return new RectInt(squareX, squareY, squareSize, squareSize);
    }

    private void PublishCapturedPhoto()
    {
        // Show the photo in the UI preview image.
        if (photoPreview != null)
        {
            photoPreview.texture = capturedPhoto;
        }

        if (addPhotoToJournal && photoJournal != null)
        {
            photoJournal.AddPhoto(capturedPhoto, lastDetectedSubjectName);
        }

        OnPhotoCaptured?.Invoke(capturedPhoto);
        OnPhotoCapturedDetailed?.Invoke(capturedPhoto, lastDetectedSubjectName);

        // If a material was assigned, put the photo on that material too.
        // If not, log a warning so the developer knows to assign one if needed.
        if (photoMaterial != null)
        {
            photoMaterial.mainTexture = capturedPhoto;
            Debug.Log("Material reference: " + photoMaterial);
            Debug.Log("Material name: " + photoMaterial.name);
        }
        else
        {
            Debug.LogWarning("photoMaterial is null. Assign it in the Inspector if you want material previews.");
        }

        TriggerCornerPreview();
        TriggerScreenFlash();
    }

    private void RestoreOverlayAfterCapture(bool crosshairWasVisible)
    {
        if (!hideCrosshairDuringCapture)
        {
            return;
        }

        // Refresh zoom state after capture, then restore previous crosshair visibility
        // for this frame so it doesn't get stuck off.
        UpdateZoomState();

        bool overlayVisible = showOverlayOnlyWhenZoomed ? isZoomed : true;

        if (captureSquareUI != null)
        {
            captureSquareUI.gameObject.SetActive(overlayVisible);
        }

        if (crosshairUI != null)
        {
            crosshairUI.SetActive(crosshairWasVisible || overlayVisible);
        }
    }

    // Returns the last picture taken (or null if none yet).
    public Texture2D GetLastPhoto()
    {
        return capturedPhoto;
    }

    public string GetLastDetectedSubjectName()
    {
        return lastDetectedSubjectName;
    }

    private void InitializeFlashOverlay()
    {
        if (flashOverlay == null)
        {
            return;
        }

        flashOverlay.raycastTarget = false;
        Color c = flashOverlay.color;
        c.a = 0f;
        flashOverlay.color = c;
    }

    private void TriggerScreenFlash()
    {
        if (!enableScreenFlash || flashOverlay == null)
        {
            return;
        }

        if (flashRoutine != null)
        {
            StopCoroutine(flashRoutine);
        }

        flashRoutine = StartCoroutine(PlayScreenFlash());
    }

    private void UpdateDetectedSubject()
    {
        lastDetectedSubjectName = DetectSubjectName();
    }

    private string DetectSubjectName()
    {
        if (!enableSubjectDetection || captureCamera == null)
        {
            return "";
        }

        Rect captureRect = GetCaptureRectOnScreen();
        Vector2 center = captureRect.center;
        Ray ray = captureCamera.ScreenPointToRay(center);

        if (!Physics.Raycast(ray, out RaycastHit hit, detectionMaxDistance, detectionLayers, QueryTriggerInteraction.Ignore))
        {
            return "";
        }

        PhotoSubject subject = hit.collider.GetComponentInParent<PhotoSubject>();
        if (subject != null)
        {
            return string.IsNullOrWhiteSpace(subject.subjectName) ? hit.collider.gameObject.name : subject.subjectName;
        }

        if (requirePhotoSubjectComponent)
        {
            return "";
        }

        string hitTag = hit.collider.tag;
        if (!string.IsNullOrWhiteSpace(hitTag) && hitTag != "Untagged")
        {
            return hitTag;
        }

        return hit.collider.gameObject.name;
    }

    private IEnumerator PlayScreenFlash()
    {
        Color c = flashColor;
        c.a = 0f;
        flashOverlay.color = c;

        float inDuration = Mathf.Max(0.001f, flashInDuration);
        float outDuration = Mathf.Max(0.001f, flashOutDuration);

        float t = 0f;
        while (t < inDuration)
        {
            t += flashUseUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            float alpha = Mathf.Lerp(0f, flashColor.a, t / inDuration);
            Color inColor = flashColor;
            inColor.a = alpha;
            flashOverlay.color = inColor;
            yield return null;
        }

        t = 0f;
        while (t < outDuration)
        {
            t += flashUseUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            float alpha = Mathf.Lerp(flashColor.a, 0f, t / outDuration);
            Color outColor = flashColor;
            outColor.a = alpha;
            flashOverlay.color = outColor;
            yield return null;
        }

        Color doneColor = flashColor;
        doneColor.a = 0f;
        flashOverlay.color = doneColor;
        flashRoutine = null;
    }

    private void InitializeCornerPreview()
    {
        if (cornerPreviewImage == null)
        {
            return;
        }

        if (cornerPreviewRect == null)
        {
            cornerPreviewRect = cornerPreviewImage.rectTransform;
        }

        if (cornerPreviewCanvasGroup == null)
        {
            cornerPreviewCanvasGroup = cornerPreviewImage.GetComponent<CanvasGroup>();
            if (cornerPreviewCanvasGroup == null)
            {
                cornerPreviewCanvasGroup = cornerPreviewImage.gameObject.AddComponent<CanvasGroup>();
            }
        }

        cornerPreviewImage.raycastTarget = false;
        cornerPreviewShownPosition = cornerPreviewRect.anchoredPosition;
        cornerPreviewShownScale = cornerPreviewRect.localScale;
        cornerPreviewShownRotationZ = cornerPreviewRect.localEulerAngles.z;
        cornerPreviewCanvasGroup.alpha = 0f;
        cornerPreviewRect.anchoredPosition = cornerPreviewShownPosition + cornerPreviewHiddenOffset;
        cornerPreviewRect.localScale = cornerPreviewShownScale * cornerPreviewStartScale;
        SetCornerPreviewRotation(cornerPreviewShownRotationZ);
        ConfigureCornerPreviewShadow();
    }

    private void TriggerCornerPreview()
    {
        if (!enableCornerPreview || cornerPreviewImage == null)
        {
            return;
        }

        if (cornerPreviewRect == null || cornerPreviewCanvasGroup == null)
        {
            InitializeCornerPreview();
        }

        if (cornerPreviewRect == null || cornerPreviewCanvasGroup == null)
        {
            return;
        }

        cornerPreviewImage.texture = capturedPhoto;

        float tilt = cornerPreviewUsePolaroidStyle ? cornerPreviewBaseTilt : 0f;
        if (cornerPreviewUsePolaroidStyle && cornerPreviewUseRandomTilt)
        {
            tilt += UnityEngine.Random.Range(-cornerPreviewRandomTiltRange, cornerPreviewRandomTiltRange);
        }
        cornerPreviewTargetTilt = tilt;

        if (cornerPreviewRoutine != null)
        {
            StopCoroutine(cornerPreviewRoutine);
        }

        cornerPreviewRoutine = StartCoroutine(PlayCornerPreview());
    }

    private IEnumerator PlayCornerPreview()
    {
        float inDuration = Mathf.Max(0.001f, cornerPreviewInDuration);
        float outDuration = Mathf.Max(0.001f, cornerPreviewOutDuration);

        Vector2 hiddenPos = cornerPreviewShownPosition + cornerPreviewHiddenOffset;
        Vector3 startScale = cornerPreviewShownScale * cornerPreviewStartScale;
        Vector3 popScale = cornerPreviewShownScale * cornerPreviewPopScale;
        float tiltSign = Mathf.Abs(cornerPreviewTargetTilt) < 0.01f ? 1f : Mathf.Sign(cornerPreviewTargetTilt);
        float startTilt = cornerPreviewTargetTilt + (tiltSign * cornerPreviewPopTilt);
        float popTilt = cornerPreviewTargetTilt - (tiltSign * (cornerPreviewPopTilt * 0.35f));

        cornerPreviewCanvasGroup.alpha = 0f;
        cornerPreviewRect.anchoredPosition = hiddenPos;
        cornerPreviewRect.localScale = startScale;
        SetCornerPreviewRotation(startTilt);

        float t = 0f;
        while (t < inDuration)
        {
            t += cornerPreviewUseUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            float p = Mathf.Clamp01(t / inDuration);
            float eased = p * p * (3f - 2f * p);

            cornerPreviewCanvasGroup.alpha = eased;
            cornerPreviewRect.anchoredPosition = Vector2.Lerp(hiddenPos, cornerPreviewShownPosition, eased);
            cornerPreviewRect.localScale = Vector3.Lerp(startScale, popScale, eased);
            SetCornerPreviewRotation(Mathf.Lerp(startTilt, popTilt, eased));
            yield return null;
        }

        // Quick settle to normal size.
        float settleDuration = Mathf.Min(0.08f, outDuration * 0.5f);
        t = 0f;
        while (t < settleDuration)
        {
            t += cornerPreviewUseUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            float p = Mathf.Clamp01(t / Mathf.Max(0.001f, settleDuration));
            cornerPreviewRect.localScale = Vector3.Lerp(popScale, cornerPreviewShownScale, p);
            SetCornerPreviewRotation(Mathf.Lerp(popTilt, cornerPreviewTargetTilt, p));
            yield return null;
        }
        cornerPreviewRect.localScale = cornerPreviewShownScale;
        SetCornerPreviewRotation(cornerPreviewTargetTilt);

        float hold = Mathf.Max(0f, cornerPreviewHoldDuration);
        if (hold > 0f)
        {
            if (cornerPreviewUseUnscaledTime)
            {
                yield return new WaitForSecondsRealtime(hold);
            }
            else
            {
                yield return new WaitForSeconds(hold);
            }
        }

        t = 0f;
        while (t < outDuration)
        {
            t += cornerPreviewUseUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            float p = Mathf.Clamp01(t / outDuration);
            float eased = p * p * (3f - 2f * p);

            cornerPreviewCanvasGroup.alpha = 1f - eased;
            cornerPreviewRect.anchoredPosition = Vector2.Lerp(cornerPreviewShownPosition, hiddenPos, eased);
            SetCornerPreviewRotation(Mathf.Lerp(cornerPreviewTargetTilt, startTilt, eased));
            yield return null;
        }

        cornerPreviewCanvasGroup.alpha = 0f;
        cornerPreviewRect.anchoredPosition = hiddenPos;
        cornerPreviewRect.localScale = startScale;
        SetCornerPreviewRotation(startTilt);
        cornerPreviewRoutine = null;
    }

    private void SetCornerPreviewRotation(float zRotation)
    {
        if (cornerPreviewRect == null)
        {
            return;
        }

        Vector3 angles = cornerPreviewRect.localEulerAngles;
        angles.z = zRotation;
        cornerPreviewRect.localEulerAngles = angles;
    }

    private void ConfigureCornerPreviewShadow()
    {
        if (cornerPreviewImage == null)
        {
            return;
        }

        Shadow shadow = cornerPreviewImage.GetComponent<Shadow>();
        if (cornerPreviewAddDropShadow)
        {
            if (shadow == null)
            {
                shadow = cornerPreviewImage.gameObject.AddComponent<Shadow>();
            }

            shadow.effectColor = cornerPreviewShadowColor;
            shadow.effectDistance = cornerPreviewShadowOffset;
            shadow.useGraphicAlpha = true;
            shadow.enabled = true;
        }
        else if (shadow != null)
        {
            shadow.enabled = false;
        }
    }
}
