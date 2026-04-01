using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class PhotoJournal : MonoBehaviour
{
    [Header("Input")]
    public KeyCode pauseKey = KeyCode.Escape;

    [Header("UI")]
    public GameObject pauseMenuRoot;
    public GameObject pauseMainPanel;
    public GameObject albumPanel;
    [Tooltip("Legacy single-slot image. Used if Album Slots list is empty.")]
    public RawImage albumImage;
    [Tooltip("Album slots shown per page. If this list has items, it overrides Album Image.")]
    public List<RawImage> albumSlots = new List<RawImage>();
    [Tooltip("Optional subject labels mapped to Album Slots by index.")]
    public List<TMP_Text> albumSubjectLabels = new List<TMP_Text>();
    [Tooltip("Optional timestamp labels mapped to Album Slots by index.")]
    public List<TMP_Text> albumTimeLabels = new List<TMP_Text>();
    public TMP_Text pageLabel;
    public TMP_Text photoInfoLabel;
    public Texture placeholderTexture;
    public string emptySlotLabel = "";
    public string unknownSubjectLabel = "Unknown";

    [Header("References")]
    public PhotoCapture photoCapture;
    public FirstPersonController firstPersonController;

    [Header("Album")]
    public bool newestFirst = false;
    [Tooltip("0 means unlimited.")]
    public int maxStoredPhotos = 0;

    [Header("Pause Behavior")]
    public bool disablePhotoCaptureWhilePaused = true;
    public bool disableControllerWhilePaused = true;
    public bool lockCursorWhenResumed = true;

    private bool isPaused;
    private int currentPage;
    private readonly List<AlbumEntry> albumEntries = new List<AlbumEntry>();

    [Serializable]
    private class AlbumEntry
    {
        public Texture2D photo;
        public string subjectName;
        public string capturedAt;
    }

    void Awake()
    {
        if (photoCapture == null)
        {
            photoCapture = FindFirstObjectByType<PhotoCapture>();
        }

        if (firstPersonController == null)
        {
            firstPersonController = FindFirstObjectByType<FirstPersonController>();
        }

        if (pauseMenuRoot != null)
        {
            pauseMenuRoot.SetActive(false);
        }

        if (pauseMainPanel != null)
        {
            pauseMainPanel.SetActive(false);
        }

        if (albumPanel != null)
        {
            albumPanel.SetActive(false);
        }
    }

    void Update()
    {
        if (Input.GetKeyDown(pauseKey))
        {
            HandlePauseKey();
        }
    }

    private void HandlePauseKey()
    {
        if (!isPaused)
        {
            PauseGame();
            return;
        }

        if (albumPanel != null && albumPanel.activeSelf)
        {
            OpenPauseMain();
            return;
        }

        ResumeGame();
    }

    public void PauseGame()
    {
        isPaused = true;
        Time.timeScale = 0f;
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;

        if (disableControllerWhilePaused && firstPersonController != null)
        {
            firstPersonController.enabled = false;
        }

        if (disablePhotoCaptureWhilePaused && photoCapture != null)
        {
            photoCapture.enabled = false;
        }

        OpenPauseMain();
    }

    public void ResumeGame()
    {
        isPaused = false;
        Time.timeScale = 1f;
        Cursor.visible = false;
        Cursor.lockState = lockCursorWhenResumed ? CursorLockMode.Locked : CursorLockMode.None;

        if (disableControllerWhilePaused && firstPersonController != null)
        {
            firstPersonController.enabled = true;
        }

        if (disablePhotoCaptureWhilePaused && photoCapture != null)
        {
            photoCapture.enabled = true;
        }

        if (pauseMenuRoot != null)
        {
            pauseMenuRoot.SetActive(false);
        }
    }

    public void OpenPauseMain()
    {
        if (!isPaused)
        {
            PauseGame();
            return;
        }

        if (pauseMenuRoot != null)
        {
            pauseMenuRoot.SetActive(true);
        }

        if (pauseMainPanel != null)
        {
            pauseMainPanel.SetActive(true);
        }

        if (albumPanel != null)
        {
            albumPanel.SetActive(false);
        }
    }

    public void OpenAlbum()
    {
        if (!isPaused)
        {
            PauseGame();
        }

        if (pauseMenuRoot != null)
        {
            pauseMenuRoot.SetActive(true);
        }

        if (pauseMainPanel != null)
        {
            pauseMainPanel.SetActive(false);
        }

        if (albumPanel != null)
        {
            albumPanel.SetActive(true);
        }

        SeedFromLastPhotoIfNeeded();
        int slotsPerPage = GetSlotsPerPage();
        int totalPages = GetTotalPages(slotsPerPage);
        currentPage = newestFirst ? 0 : Mathf.Max(0, totalPages - 1);
        RefreshAlbumPage();
    }

    public void BackFromAlbum()
    {
        OpenPauseMain();
    }

    public void NextAlbumPage()
    {
        int slotsPerPage = GetSlotsPerPage();
        if (slotsPerPage <= 0)
        {
            return;
        }

        int totalPages = GetTotalPages(slotsPerPage);
        if (currentPage < totalPages - 1)
        {
            currentPage++;
            RefreshAlbumPage();
        }
    }

    public void PreviousAlbumPage()
    {
        if (currentPage > 0)
        {
            currentPage--;
            RefreshAlbumPage();
        }
    }

    // Optional aliases for UI buttons.
    public void NextPage()
    {
        NextAlbumPage();
    }

    public void PreviousPage()
    {
        PreviousAlbumPage();
    }

    public void AddPhoto(Texture2D photo)
    {
        AddPhoto(photo, "");
    }

    public void AddPhoto(Texture2D photo, string subjectName)
    {
        if (photo == null)
        {
            return;
        }

        AlbumEntry entry = new AlbumEntry
        {
            photo = photo,
            subjectName = string.IsNullOrWhiteSpace(subjectName) ? "Unknown" : subjectName,
            capturedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
        };

        albumEntries.Add(entry);

        if (maxStoredPhotos > 0 && albumEntries.Count > maxStoredPhotos)
        {
            albumEntries.RemoveAt(0);
        }

        if (isPaused && albumPanel != null && albumPanel.activeSelf)
        {
            if (!newestFirst)
            {
                int slotsPerPage = GetSlotsPerPage();
                int totalPages = GetTotalPages(slotsPerPage);
                currentPage = Mathf.Max(0, totalPages - 1);
            }

            RefreshAlbumPage();
        }
    }

    private void RefreshAlbumPage()
    {
        RawImage[] slots = GetSlots();
        int slotsPerPage = slots.Length;

        if (slotsPerPage == 0)
        {
            return;
        }

        int totalPages = GetTotalPages(slotsPerPage);
        currentPage = Mathf.Clamp(currentPage, 0, Mathf.Max(0, totalPages - 1));

        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] != null)
            {
                slots[i].texture = placeholderTexture;
            }

            SetSlotLabels(i, emptySlotLabel, "");
        }

        AlbumEntry firstEntryOnPage = null;
        int startIndex = currentPage * slotsPerPage;

        for (int i = 0; i < slotsPerPage; i++)
        {
            int logicalIndex = startIndex + i;
            int entryIndex = newestFirst
                ? albumEntries.Count - 1 - logicalIndex
                : logicalIndex;

            if (entryIndex < 0 || entryIndex >= albumEntries.Count)
            {
                continue;
            }

            AlbumEntry entry = albumEntries[entryIndex];
            if (slots[i] != null)
            {
                slots[i].texture = entry.photo;
            }
            SetSlotLabels(i, entry.subjectName, entry.capturedAt);

            if (firstEntryOnPage == null)
            {
                firstEntryOnPage = entry;
            }
        }

        if (pageLabel != null)
        {
            pageLabel.text = "Page " + (currentPage + 1) + " / " + totalPages;
        }

        if (photoInfoLabel != null)
        {
            if (firstEntryOnPage == null)
            {
                photoInfoLabel.text = "No photos yet";
            }
            else
            {
                photoInfoLabel.text = "Subject: " + firstEntryOnPage.subjectName + "\nTaken: " + firstEntryOnPage.capturedAt;
            }
        }
    }

    private RawImage[] GetSlots()
    {
        List<RawImage> configured = new List<RawImage>();

        for (int i = 0; i < albumSlots.Count; i++)
        {
            if (albumSlots[i] != null)
            {
                configured.Add(albumSlots[i]);
            }
        }

        if (configured.Count == 0 && albumImage != null)
        {
            configured.Add(albumImage);
        }

        return configured.ToArray();
    }

    private int GetSlotsPerPage()
    {
        return GetSlots().Length;
    }

    private int GetTotalPages(int slotsPerPage)
    {
        if (slotsPerPage <= 0)
        {
            return 1;
        }

        int count = Mathf.Max(1, albumEntries.Count);
        return Mathf.Max(1, Mathf.CeilToInt((float)count / slotsPerPage));
    }

    private void SetSlotLabels(int slotIndex, string subject, string capturedAt)
    {
        bool isEmptySlot = string.IsNullOrWhiteSpace(capturedAt) && string.IsNullOrWhiteSpace(subject);

        if (slotIndex >= 0 && slotIndex < albumSubjectLabels.Count && albumSubjectLabels[slotIndex] != null)
        {
            string subjectValue = isEmptySlot
                ? emptySlotLabel
                : (string.IsNullOrWhiteSpace(subject) ? unknownSubjectLabel : subject);
            albumSubjectLabels[slotIndex].text = subjectValue;
        }

        if (slotIndex >= 0 && slotIndex < albumTimeLabels.Count && albumTimeLabels[slotIndex] != null)
        {
            albumTimeLabels[slotIndex].text = capturedAt ?? "";
        }
    }

    private void SeedFromLastPhotoIfNeeded()
    {
        if (albumEntries.Count > 0 || photoCapture == null)
        {
            return;
        }

        Texture2D lastPhoto = photoCapture.GetLastPhoto();
        if (lastPhoto == null)
        {
            return;
        }

        string subjectName = photoCapture.GetLastDetectedSubjectName();
        albumEntries.Add(new AlbumEntry
        {
            photo = lastPhoto,
            subjectName = string.IsNullOrWhiteSpace(subjectName) ? "Unknown" : subjectName,
            capturedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
        });
    }

    void OnDestroy()
    {
        if (isPaused)
        {
            Time.timeScale = 1f;
        }
    }
}
