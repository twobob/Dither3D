using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using static System.Net.Mime.MediaTypeNames;
using System.Diagnostics;

/// <summary>
/// Loads and plays a GIF, assigning each frame to the material’s '_DepthTex' texture.
/// </summary>
public class UniGifDepthMaterial : MonoBehaviour
{
    /// <summary>
    /// Script state
    /// </summary>
    public enum State
    {
        None,
        Loading,
        Ready,
        Playing,
        Pause
    }

    [Header("GIF Setup")]
    [Tooltip("When true, automatically load from the below URL at Start.")]
    [SerializeField]
    private bool m_loadOnStart = false;

    [Tooltip("GIF image URL (web address or file in StreamingAssets).")]
    [SerializeField]
    private string m_loadOnStartUrl = "";

    [Header("Texture Settings")]
    [Tooltip("Filter mode for the decoded GIF frames.")]
    [SerializeField]
    private FilterMode m_filterMode = FilterMode.Point;

    [Tooltip("Wrap mode for the decoded GIF frames.")]
    [SerializeField]
    private TextureWrapMode m_wrapMode = TextureWrapMode.Clamp;

    [Header("Target Material/Animation")]
    [Tooltip("Optional: Assign a specific Renderer here, or else we'll fetch the local one.")]
    [SerializeField]
    private Renderer m_targetRenderer;

    [Tooltip("If true, spins the object while loading the GIF.")]
    [SerializeField]
    private bool m_rotateOnLoading = false;

    [Tooltip("If true, logs debug information to the console.")]
    [SerializeField]
    private bool m_outputDebugLog = false;

    // Internal GIF data
    private List<UniGif.GifTexture> m_gifTextureList = null;
    private float m_delayTime = 0f;
    private int m_gifTextureIndex = 0;
    private int m_nowLoopCount = 0;

    /// <summary>
    /// Current state
    /// </summary>
    public State nowState { get; private set; }

    /// <summary>
    /// How many times the GIF will loop (0 means infinite)
    /// </summary>
    public int loopCount { get; private set; }

    /// <summary>
    /// Decoded GIF width in pixels
    /// </summary>
    public int width { get; private set; }

    /// <summary>
    /// Decoded GIF height in pixels
    /// </summary>
    public int height { get; private set; }

    private void Start()
    {
        // Retrieve the target Renderer if none assigned
        if (m_targetRenderer == null)
        {
            m_targetRenderer = GetComponent<Renderer>();
        }

        // By accessing .material now, Unity will clone an instance of the shared material
        // so we can adjust textures without affecting other objects
        if (m_targetRenderer != null)
        {
            Material matInstance = m_targetRenderer.material;
        }

        if (m_loadOnStart && !string.IsNullOrEmpty(m_loadOnStartUrl))
        {
            SetGifFromUrl(m_loadOnStartUrl);
        }
    }

    private void OnDestroy()
    {
        // Clear out any dynamically allocated textures
        Clear();

        // Destroy the instantiated material, if any
        if (m_targetRenderer != null && m_targetRenderer.material != null)
        {
            Destroy(m_targetRenderer.material);
        }
    }

    private void Update()
    {
        switch (nowState)
        {
            case State.None:
                // Do nothing
                break;

            case State.Loading:
                // Optionally rotate while loading
                if (m_rotateOnLoading)
                {
                    transform.Rotate(0f, 0f, 30f * Time.deltaTime, Space.Self);
                }
                break;

            case State.Ready:
                // GIF is loaded but not playing
                break;

            case State.Playing:
                if (m_targetRenderer == null || m_gifTextureList == null || m_gifTextureList.Count == 0)
                {
                    return;
                }

                // Check if it's time to show the next frame
                if (Time.time >= m_delayTime)
                {
                    m_gifTextureIndex++;
                    if (m_gifTextureIndex >= m_gifTextureList.Count)
                    {
                        m_gifTextureIndex = 0;
                        if (loopCount > 0)
                        {
                            m_nowLoopCount++;
                            if (m_nowLoopCount >= loopCount)
                            {
                                Stop();
                                return;
                            }
                        }
                    }



                    // Update the depth texture on the material
                    m_targetRenderer.material.SetTexture("_DepthTex", m_gifTextureList[m_gifTextureIndex].m_texture2d);

                    m_targetRenderer.material.SetTexture("_MainTex", m_gifTextureList[m_gifTextureIndex].m_texture2d);

                    m_targetRenderer.material.SetTexture("_BumpMap", m_gifTextureList[m_gifTextureIndex].m_texture2d);

                    m_delayTime = Time.time + m_gifTextureList[m_gifTextureIndex].m_delaySec;
                }
                break;

            case State.Pause:
                // Animation paused; do nothing
                break;
        }
    }

    /// <summary>
    /// Initiates loading of the GIF at the given URL
    /// </summary>
    /// <param name="url">Path to GIF, either http/https or a file in StreamingAssets.</param>
    /// <param name="autoPlay">If true, automatically begins animating once loaded.</param>
    public void SetGifFromUrl(string url, bool autoPlay = true)
    {
        StartCoroutine(SetGifFromUrlCoroutine(url, autoPlay));
    }

    /// <summary>
    /// Coroutine to load the GIF from a specified URL.
    /// </summary>
    private IEnumerator SetGifFromUrlCoroutine(string url, bool autoPlay)
    {
        if (string.IsNullOrEmpty(url))
        {
            UnityEngine.Debug.LogError("No URL specified.");
            yield break;
        }

        if (nowState == State.Loading)
        {
            UnityEngine.Debug.LogWarning("Already loading a GIF.");
            yield break;
        }

        nowState = State.Loading;

        // Construct the path
        string path;
        if (url.StartsWith("http"))
        {
            path = url;
        }
        else
        {
            // Local file in StreamingAssets
            path = Path.Combine("file:///" + UnityEngine.Application.streamingAssetsPath, url);
        }

        // Load the file
        using (WWW www = new WWW(path))
        {
            yield return www;

            if (!string.IsNullOrEmpty(www.error))
            {
                UnityEngine.Debug.LogError("Failed to load file: " + www.error);
                nowState = State.None;
                yield break;
            }

            // Clear existing data
            Clear();
            nowState = State.Loading;

            // Decode GIF frames
            yield return StartCoroutine(UniGif.GetTextureListCoroutine(
                www.bytes,
                (gifTexList, loopCount, width, height) =>
                {
                    if (gifTexList != null)
                    {
                        m_gifTextureList = gifTexList;
                        this.loopCount = loopCount;
                        this.width = width;
                        this.height = height;
                        nowState = State.Ready;

                        // Reset rotation if we were spinning
                        if (m_rotateOnLoading)
                        {
                            transform.localEulerAngles = Vector3.zero;
                        }

                        // Optionally auto-play
                        if (autoPlay)
                        {
                            Play();
                        }
                        else
                        {
                            // Set first frame on the material
                            if (m_targetRenderer != null && m_gifTextureList.Count > 0)
                            {
                                m_targetRenderer.material.SetTexture("_DepthTex", m_gifTextureList[0].m_texture2d);
                            }
                        }
                    }
                    else
                    {
                        UnityEngine.Debug.LogError("Failed to decode GIF data.");
                        nowState = State.None;
                    }
                },
                m_filterMode,
                m_wrapMode,
                m_outputDebugLog
            ));
        }
    }

    /// <summary>
    /// Clears the current GIF data and resets the state.
    /// </summary>
    public void Clear()
    {
        // Clear the material’s depth texture
        if (m_targetRenderer != null)
        {
            m_targetRenderer.material.SetTexture("_DepthTex", null);
        }

        // Destroy all decoded textures
        if (m_gifTextureList != null)
        {
            for (int i = 0; i < m_gifTextureList.Count; i++)
            {
                if (m_gifTextureList[i] != null)
                {
                    if (m_gifTextureList[i].m_texture2d != null)
                    {
                        Destroy(m_gifTextureList[i].m_texture2d);
                        m_gifTextureList[i].m_texture2d = null;
                    }
                    m_gifTextureList[i] = null;
                }
            }
            m_gifTextureList.Clear();
            m_gifTextureList = null;
        }

        nowState = State.None;
    }

    /// <summary>
    /// Begin playing the GIF animation from the first frame.
    /// </summary>
    public void Play()
    {
        if (nowState != State.Ready)
        {
            UnityEngine.Debug.LogWarning("Cannot play; state is not READY.");
            return;
        }
        if (m_targetRenderer == null || m_gifTextureList == null || m_gifTextureList.Count == 0)
        {
            UnityEngine.Debug.LogError("No target renderer or GIF frames available.");
            return;
        }

        nowState = State.Playing;
        m_gifTextureIndex = 0;
        m_nowLoopCount = 0;

        // Apply the first frame
        m_targetRenderer.material.SetTexture("_DepthTex", m_gifTextureList[0].m_texture2d);
        m_delayTime = Time.time + m_gifTextureList[0].m_delaySec;
    }

    /// <summary>
    /// Stop the animation and revert to the READY state.
    /// </summary>
    public void Stop()
    {
        if (nowState != State.Playing && nowState != State.Pause)
        {
            UnityEngine.Debug.LogWarning("Cannot stop; state is not Playing or Paused.");
            return;
        }
        nowState = State.Ready;
    }

    /// <summary>
    /// Temporarily pause the animation.
    /// </summary>
    public void Pause()
    {
        if (nowState != State.Playing)
        {
            UnityEngine.Debug.LogWarning("Cannot pause; state is not Playing.");
            return;
        }
        nowState = State.Pause;
    }

    /// <summary>
    /// Resume the animation from a paused state.
    /// </summary>
    public void Resume()
    {
        if (nowState != State.Pause)
        {
            UnityEngine.Debug.LogWarning("Cannot resume; state is not Paused.");
            return;
        }
        nowState = State.Playing;
    }
}
