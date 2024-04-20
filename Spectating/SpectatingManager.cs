using BaboonAPI.Hooks.Tracks;
using HarmonyLib;
using Microsoft.FSharp.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using TootTallyAccounts;
using TootTallyCore.APIServices;
using TootTallyCore.Utils.Helpers;
using TootTallyCore.Utils.TootTallyGlobals;
using TootTallyCore.Utils.TootTallyNotifs;
using TootTallyGameModifiers;
using TootTallyTrombuddies;
using UnityEngine;
using UnityEngine.Playables;

namespace TootTallySpectator
{
    public class SpectatingManager : MonoBehaviour
    {
        public static JsonConverter[] _dataConverter = new JsonConverter[] { new SocketDataConverter() };
        private static List<SpectatingSystem> _spectatingSystemList;
        public static SpectatingSystem hostedSpectatingSystem;
        public static int[] currentSpectatorIDList;
        public static bool IsHosting => hostedSpectatingSystem != null && hostedSpectatingSystem.IsConnected && hostedSpectatingSystem.IsHost;
        public static bool IsSpectating => _spectatingSystemList != null && !IsHosting && _spectatingSystemList.Any(x => x.IsConnected);

        public static SocketSpectatorInfo nullSpecInfo = new SocketSpectatorInfo() { count = 0 };

        public void Awake()
        {
            _spectatingSystemList ??= new List<SpectatingSystem>();
            if (Plugin.Instance.AllowSpectate.Value && TootTallyUser.userInfo != null && TootTallyUser.userInfo.id != 0)
                CreateUniqueSpectatingConnection(TootTallyUser.userInfo.id, TootTallyUser.userInfo.username);
            Plugin.Instance.StartCoroutine(TootTallyAPIService.GetSpectatorIDList(idList => currentSpectatorIDList = idList));
        }

        public void Update()
        {
            _spectatingSystemList?.ForEach(s => s.UpdateStacks());

            if (Input.GetKey(KeyCode.LeftShift) && Input.GetKeyDown(KeyCode.Escape) && _spectatingSystemList.Count > 0 && !IsHosting)
            {
                SpectatingOverlay.UpdateViewerList(nullSpecInfo);
                SpectatingOverlay.HideStopSpectatingButton();
                SpectatingOverlay.HideViewerIcon();
                _spectatingSystemList.Last().RemoveFromManager();
            }

        }

        public static void StopAllSpectator()
        {
            if (_spectatingSystemList != null && _spectatingSystemList.Count > 0)
            {
                SpectatingOverlay.UpdateViewerList(nullSpecInfo);
                SpectatingOverlay.SetCurrentUserState(UserState.None);
                for (int i = 0; i < _spectatingSystemList.Count;)
                    RemoveSpectator(_spectatingSystemList[i]);
                if (hostedSpectatingSystem != null && hostedSpectatingSystem.IsConnected)
                    hostedSpectatingSystem = null;
            }
            TootTallyGlobalVariables.isSpectating = IsSpectating;
        }

        public static void CancelPendingConnections()
        {

            var toRemoveList = _spectatingSystemList?.Where(x => x.ConnectionPending);
            if (toRemoveList.Count() > 0)
            {
                List<SpectatingSystem> canceledSpec = new();
                toRemoveList.ToList().ForEach(spec =>
                {
                    spec.CancelConnection();
                    canceledSpec.Add(spec);
                });
                canceledSpec.ForEach(RemoveSpectator);
            }
        }

        public static SpectatingSystem CreateNewSpectatingConnection(int id, string name)
        {
            var spec = new SpectatingSystem(id, name);
            spec.OnWebSocketOpenCallback = OnSpectatingConnect;
            _spectatingSystemList.Add(spec);
            if (id == TootTallyUser.userInfo.id)
                hostedSpectatingSystem = spec;
            return spec;
        }

        public static void OnSpectatingConnect(SpectatingSystem sender)
        {
            if (!sender.IsHost)
            {
                sender.OnSocketSongInfoReceived = SpectatingManagerPatches.OnSongInfoReceived;
                sender.OnSocketUserStateReceived = SpectatingManagerPatches.OnUserStateReceived;
                sender.OnSocketFrameDataReceived = SpectatingManagerPatches.OnFrameDataReceived;
                sender.OnSocketTootDataReceived = SpectatingManagerPatches.OnTootDataReceived;
                sender.OnSocketNoteDataReceived = SpectatingManagerPatches.OnNoteDataReceived;
                TootTallyNotifManager.DisplayNotif($"Waiting for host to pick a song...");
                TootTallyGlobalVariables.isSpectating = true;
            }
            else
            {
                OnHostConnection();
                SpectatingManagerPatches.SendCurrentUserState();
            }
            sender.OnSocketSpecInfoReceived = SpectatingManagerPatches.OnSpectatorDataReceived;
        }

        public static void RemoveSpectator(SpectatingSystem spectator)
        {
            if (spectator == null) return;

            if (spectator.IsConnected)
                spectator.Disconnect();
            if (_spectatingSystemList.Contains(spectator))
                _spectatingSystemList.Remove(spectator);
            else
                Plugin.LogInfo($"Couldnt find websocket in list.");
        }

        public static SpectatingSystem CreateUniqueSpectatingConnection(int id, string name)
        {
            StopAllSpectator();
            return CreateNewSpectatingConnection(id, name);
        }

        public static void OnAllowHostConfigChange(bool value)
        {
            if (value && hostedSpectatingSystem == null && TootTallyUser.userInfo.id != 0)
                CreateUniqueSpectatingConnection(TootTallyUser.userInfo.id, TootTallyUser.userInfo.username);
            else if (!value && hostedSpectatingSystem != null)
            {
                RemoveSpectator(hostedSpectatingSystem);
                hostedSpectatingSystem = null;
            }
        }

        public static void UpdateSpectatorIDList()
        {
            Plugin.Instance.StartCoroutine(TootTallyAPIService.GetSpectatorIDList(idList => currentSpectatorIDList = idList));
        }

        public static void OnHostConnection()
        {
        }

        public static bool IsAnyConnectionPending() => _spectatingSystemList.Any(x => x.ConnectionPending);

        public static void OnSpectateButtonPress(int id, string name)
        {
            if (!IsAnyConnectionPending() && !(TootTallyUser.userInfo.id == id && IsHosting))
            {
                CreateUniqueSpectatingConnection(id, name);
                TrombuddiesManager.UpdateUsers();
            }
        }

        public enum DataType
        {
            UserState,
            SongInfo,
            FrameData,
            TootData,
            NoteData,
            SpectatorInfo,
        }

        public enum UserState
        {
            None,
            SelectingSong,
            Paused,
            Playing,
            Restarting,
            Quitting,
            PointScene,
            GettingReady,
        }
        public enum SceneType
        {
            None,
            LevelSelect,
            GameController,
            HomeController
        }

        public interface ISocketMessage
        {
            public string dataType { get; }
        }

        public struct SocketUserState : ISocketMessage
        {

            public int userState { get; set; }

            public string dataType => DataType.UserState.ToString();
        }

        public struct SocketFrameData : ISocketMessage
        {
            public float time { get; set; }
            public float noteHolder { get; set; }
            public float pointerPosition { get; set; }
            public string dataType => DataType.FrameData.ToString();
        }

        public struct SocketTootData : ISocketMessage
        {
            public float time { get; set; }
            public float noteHolder { get; set; }
            public bool isTooting { get; set; }
            public string dataType => DataType.TootData.ToString();
        }

        public struct SocketSongInfo : ISocketMessage
        {
            public string trackRef { get; set; }
            public int songID { get; set; }
            public float gameSpeed { get; set; }
            public float scrollSpeed { get; set; }
            public string gamemodifiers { get; set; }
            public string dataType => DataType.SongInfo.ToString();
        }

        public struct SocketNoteData : ISocketMessage
        {
            public int noteID { get; set; }
            public double noteScoreAverage { get; set; }
            public bool champMode { get; set; }
            public int multiplier { get; set; }
            public int totalScore { get; set; }
            public bool releasedButtonBetweenNotes { get; set; }
            public float health { get; set; }
            public int highestCombo { get; set; }
            public string dataType => DataType.NoteData.ToString();
        }

        public struct SocketSpectatorInfo : ISocketMessage
        {
            public string hostName { get; set; }
            public int count { get; set; }
            public List<string> spectators { get; set; }
            public string dataType => DataType.SpectatorInfo.ToString();
        }

        public class SocketDataConverter : JsonConverter
        {
            public override bool CanConvert(Type objectType) => objectType == typeof(ISocketMessage);

            public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
            {
                JObject jo = JObject.Load(reader);
                return Enum.Parse(typeof(DataType), jo["dataType"].Value<string>()) switch
                {
                    DataType.UserState => jo.ToObject<SocketUserState>(serializer),
                    DataType.FrameData => jo.ToObject<SocketFrameData>(serializer),
                    DataType.TootData => jo.ToObject<SocketTootData>(serializer),
                    DataType.SongInfo => jo.ToObject<SocketSongInfo>(serializer),
                    DataType.NoteData => jo.ToObject<SocketNoteData>(serializer),
                    DataType.SpectatorInfo => jo.ToObject<SocketSpectatorInfo>(serializer),
                    _ => null
                };
            }

            public override bool CanWrite
            {
                get { return false; }
            }

            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
            {
                throw new NotImplementedException();
            }
        }

        #region patches
        public static class SpectatingManagerPatches
        {
            private static LevelSelectController _levelSelectControllerInstance;
            private static GameController _gameControllerInstance;
            private static PointSceneController _pointSceneControllerInstance;

            private static UserState _lastSpecState = UserState.None, _currentSpecState = UserState.None;

            private static List<SocketFrameData> _frameData = new List<SocketFrameData>();
            private static List<SocketTootData> _tootData = new List<SocketTootData>();
            private static List<SocketNoteData> _noteData = new List<SocketNoteData>();

            private static SocketFrameData _lastFrame, _currentFrame;
            private static SocketTootData _currentTootData;
            private static SocketNoteData _currentNoteData;

            private static SocketSongInfo _lastSongInfo;
            private static SocketSongInfo _currentSongInfo;

            private static TromboneTrack _lastTrackData;

            private static bool _isTooting;
            private static int _frameIndex;
            private static int _tootIndex;
            private static bool _waitingToSync;
            private static bool _spectatingStarting;
            private static bool _wasSpectating;

            [HarmonyPatch(typeof(HomeController), nameof(HomeController.Start))]
            [HarmonyPostfix]
            public static void InitOverlay() { SpectatingOverlay.Initialize(); }

            [HarmonyPatch(typeof(LevelSelectController), nameof(LevelSelectController.Start))]
            [HarmonyPostfix]
            public static void SetLevelSelectUserStatusOnAdvanceSongs(LevelSelectController __instance)
            {
                _gameControllerInstance = null;
                _pointSceneControllerInstance = null;
                _levelSelectControllerInstance = __instance;
                _spectatingStarting = false;
                _lastSongInfo.trackRef = "";
                _wasSpectating = false;
                if (IsHosting)
                    SetCurrentUserState(UserState.SelectingSong);
                else if (IsSpectating)
                    SpectatingOverlay.SetCurrentUserState(UserState.SelectingSong);
                else if (Plugin.Instance.AllowSpectate.Value && TootTallyUser.userInfo.id != 0)
                {
                    CreateUniqueSpectatingConnection(TootTallyUser.userInfo.id, TootTallyUser.userInfo.username); //Remake Hosting connection just in case it wasnt reopened correctly
                    SpectatingOverlay.HideViewerIcon();
                }
                SpectatingOverlay.HidePauseText();
                SpectatingOverlay.HideMarquee();
            }

            [HarmonyPatch(typeof(GameController), nameof(GameController.Start))]
            [HarmonyPostfix]
            public static void OnGameControllerStart(GameController __instance)
            {
                if (IsHosting && _currentHostState != UserState.GettingReady)
                {
                    if (_lastHostSongInfo.trackRef != "")
                        hostedSpectatingSystem.SendSongInfoToSocket(_lastHostSongInfo);
                    SetCurrentUserState(UserState.GettingReady);
                }
                _isQuickRestarting = false;
                _pointSceneControllerInstance = null;
                _levelSelectControllerInstance = null;
                _gameControllerInstance = __instance;
                _waitingToSync = _wasSpectating = IsSpectating;
                if (IsSpectating)
                {
                    _frameIndex = 0;
                    _tootIndex = 0;
                    _lastFrame.time = 0;
                    _currentFrame.time = _currentFrame.noteHolder = _currentFrame.pointerPosition = 0;
                    _currentTootData.time = _currentFrame.noteHolder = 0;
                    _currentTootData.isTooting = false;
                    _isTooting = false;
                    _elapsedTime = 0;
                    if (_lastTrackData != null)
                        SpectatingOverlay.ShowMarquee(_spectatingSystemList.Last().spectatorName, _lastTrackData.trackname_short, _lastSongInfo.gameSpeed, _lastSongInfo.gamemodifiers);
                }
            }

            private const float SYNC_BUFFER = 1f;

            [HarmonyPatch(typeof(GameController), nameof(GameController.playsong))]
            [HarmonyPrefix]
            public static bool OverwriteStartSongIfSyncRequired(GameController __instance)
            {
                if (!IsSpectating || IsHosting) return true;

                if (ShouldWaitForSync(out _waitingToSync))
                    TootTallyNotifManager.DisplayNotif("Waiting to sync with host...");

                return !_waitingToSync;
            }

            private static bool ShouldWaitForSync(out bool waitForSync)
            {
                waitForSync = true;

                if (_frameData != null && _frameData.Count > 0 && _frameData.Last().time >= SYNC_BUFFER && _currentSpecState != UserState.GettingReady && _currentSpecState != UserState.Restarting)
                {
                    SpectatingOverlay.SetCurrentUserState(UserState.Playing);
                    waitForSync = false;
                }
                return waitForSync;
            }

            [HarmonyPatch(typeof(GameController), nameof(GameController.playsong))]
            [HarmonyPostfix]
            public static void SetPlayingUserStatus(GameController __instance)
            {
                if (IsHosting)
                    SetCurrentUserState(UserState.Playing);

                if (IsSpectating)
                {
                    if (_frameData != null && _frameData.Count > 0 && _frameData[_frameIndex].time - __instance.musictrack.time >= SYNC_BUFFER)
                    {
                        Plugin.LogInfo("Syncing track with replay data...");
                        _specTracktime = __instance.musictrack.time = (float)_frameData[_frameIndex].time;
                        __instance.noteholderr.anchoredPosition = new Vector2((float)_frameData[_frameIndex].noteHolder, __instance.noteholderr.anchoredPosition.y);
                    }
                }
            }

            private static bool _lastIsTooting;

            [HarmonyPatch(typeof(GameController), nameof(GameController.isNoteButtonPressed))]
            [HarmonyPostfix]
            public static void GameControllerIsNoteButtonPressedPostfixPatch(GameController __instance, ref bool __result)
            {
                if (IsSpectating)
                    __result = _isTooting;
                else if (IsHosting && _lastIsTooting != __result && !__instance.paused && !__instance.retrying && !__instance.quitting)
                    hostedSpectatingSystem.SendTootData(__instance.musictrack.time - (GlobalVariables.localsettings.latencyadjust/1000f), __instance.noteholderr.anchoredPosition.x, __result);
                _lastIsTooting = __result;
            }

            [HarmonyPatch(typeof(PointSceneController), nameof(PointSceneController.Start))]
            [HarmonyPostfix]
            public static void OnPointSceneControllerStartSetInstance(PointSceneController __instance)
            {
                _levelSelectControllerInstance = null;
                _gameControllerInstance = null;
                _pointSceneControllerInstance = __instance;
                if (IsHosting)
                    SetCurrentUserState(UserState.PointScene);
                SpectatingOverlay.HideViewerIcon();
                SpectatingOverlay.HideStopSpectatingButton();
                SpectatingOverlay.HideMarquee();
            }

            [HarmonyPatch(typeof(PointSceneController), nameof(PointSceneController.Update))]
            [HarmonyPostfix]
            public static void GetOutOfPointSceneIfHostLeft(PointSceneController __instance)
            {
                if (IsSpectating && _lastSpecState == UserState.PointScene && !__instance.clickedleave)
                    if (_currentSpecState == UserState.SelectingSong)
                        BackToLevelSelect();
                    else if (_currentSpecState == UserState.Playing)
                        RetryFromPointScene();
            }

            private static float _specTracktime;

            public static void PlaybackSpectatingData(GameController __instance, float time)
            {
                Cursor.visible = true;
                if (_frameData == null || _tootData == null) return;

                if (!__instance.controllermode) __instance.controllermode = true; //Still required to not make the mouse position update

                if (_frameData.Count > 0)
                    PlaybackFrameData(time, __instance);

                if (_tootData.Count > 0)
                    PlaybackTootData(time);

                if (_frameData.Count > _frameIndex)
                    InterpolateCursorPosition(time, __instance);


            }

            private static void InterpolateCursorPosition(float trackTime, GameController __instance)
            {
                if (_currentFrame.time - _lastFrame.time > 0)
                {
                    var by = (trackTime - _lastFrame.time) / (_currentFrame.time - _lastFrame.time);
                    var newCursorPosition = Mathf.Clamp(EasingHelper.Lerp(_lastFrame.pointerPosition, _currentFrame.pointerPosition, by), -300, 300); //Safe clamping just in case
                    SetCursorPosition(__instance, newCursorPosition);
                    __instance.puppet_humanc.doPuppetControl(-newCursorPosition / 225); //225 is half of the Gameplay area:450
                }
                else
                    SetCursorPosition(__instance, _currentFrame.pointerPosition);
                
            }

            private static void PlaybackFrameData(float trackTime, GameController __instance)
            {
                if (_lastFrame.time != _currentFrame.time && trackTime >= _currentFrame.time)
                    _lastFrame = _currentFrame;

                if (_frameData.Count > _frameIndex && (_currentFrame.time == -1 || trackTime >= _currentFrame.time))
                {
                    _frameIndex = _frameData.FindIndex(_frameIndex > 1 ? _frameIndex - 1 : 0, x => trackTime < x.time);
                    if (_frameData.Count > _frameIndex && _frameIndex != -1)
                        _currentFrame = _frameData[_frameIndex];
                }
            }

            private static void SetCursorPosition(GameController __instance, float newPosition)
            {
                Vector3 pointerPosition = __instance.pointer.transform.localPosition;
                pointerPosition.y = newPosition;
                __instance.pointer.transform.localPosition = pointerPosition;
            }

            public static void OnFrameDataReceived(SocketFrameData frameData)
            {
                _frameData?.Add(frameData);
            }

            public static void OnTootDataReceived(SocketTootData tootData)
            {
                _tootData?.Add(tootData);
            }

            public static void OnNoteDataReceived(SocketNoteData noteData)
            {
                _noteData?.Add(noteData);
            }

            public static void OnSpectatorDataReceived(SocketSpectatorInfo specData)
            {
                SpectatingOverlay.UpdateViewerList(specData);
            }

            public static void PlaybackTootData(float trackTime)
            {
                if (trackTime >= _currentTootData.time && _isTooting != _currentTootData.isTooting)
                    _isTooting = _currentTootData.isTooting;

                if (_tootData.Count > _tootIndex && trackTime >= _currentTootData.time) //smaller or equal to because noteholder goes toward negative
                    _currentTootData = _tootData[_tootIndex++];
            }

            public static void OnSongInfoReceived(SocketSongInfo info)
            {
                if (info.trackRef == "" || info.gameSpeed <= 0f)
                {
                    Plugin.LogInfo("SongInfo went wrong.");
                    return;
                }
                _lastSongInfo = info;
            }

            public static void OnUserStateReceived(SocketUserState userState)
            {
                if (IsSpectating)
                    UserStateHandler((UserState)userState.userState);
            }

            private static void UserStateHandler(UserState state)
            {
                _lastSpecState = _currentSpecState;
                _currentSpecState = state;
                switch (state)
                {
                    case UserState.SelectingSong:
                        if (_pointSceneControllerInstance != null)
                            BackToLevelSelect();
                        break;

                    case UserState.Playing:
                        if (_levelSelectControllerInstance != null)
                            TryStartSong();
                        else if (_gameControllerInstance != null && _lastSpecState == UserState.Paused)
                            ResumeSong();
                        else if (_pointSceneControllerInstance != null)
                            RetryFromPointScene();
                        break;

                    case UserState.Paused:
                        if (_gameControllerInstance != null)
                            PauseSong();
                        break;

                    case UserState.Quitting:
                        if (_gameControllerInstance != null && _waitingToSync)
                            ClearSpectatingData();
                        if (_gameControllerInstance != null)
                            QuitSong();
                        break;

                    case UserState.Restarting:
                        if (_gameControllerInstance != null && _waitingToSync)
                            ClearSpectatingData();
                        if (_gameControllerInstance != null)
                            RestartSong();
                        break;

                    case UserState.GettingReady:
                        if (_levelSelectControllerInstance != null)
                            TryStartSong();
                        else if (_gameControllerInstance != null)
                            _waitingToSync = true;
                        break;
                }
            }


            private static void BackToLevelSelect()
            {
                _lastSongInfo.trackRef = "";
                _pointSceneControllerInstance.clickCont();
            }

            private static void RetryFromPointScene()
            {
                ClearSpectatingData();
                TootTallyGlobalVariables.gameSpeedMultiplier = _lastSongInfo.gameSpeed;
                GlobalVariables.gamescrollspeed = _lastSongInfo.scrollSpeed;
                Plugin.LogInfo("ScrollSpeed Set: " + _lastSongInfo.scrollSpeed);
                _pointSceneControllerInstance.clickRetry();
            }

            private static void ClearSpectatingData()
            {
                _frameData.Clear();
                _tootData.Clear();
                _noteData.Clear();
                _frameIndex = 0;
                _tootIndex = 0;
                _isTooting = false;
            }

            private static void TryStartSong()
            {
                if (_currentSpecState != UserState.SelectingSong)
                    if (_lastSongInfo.trackRef != null)
                        if (!FSharpOption<TromboneTrack>.get_IsNone(TrackLookup.tryLookup(_lastSongInfo.trackRef)))
                        {
                            _lastTrackData = TrackLookup.lookup(_lastSongInfo.trackRef);
                            _currentSongInfo = _lastSongInfo;
                            _spectatingStarting = true;
                            ClearSpectatingData();
                            TootTallyGlobalVariables.gameSpeedMultiplier = _lastSongInfo.gameSpeed;
                            GlobalVariables.gamescrollspeed = _lastSongInfo.scrollSpeed;
                            Plugin.LogInfo("ScrollSpeed Set: " + _lastSongInfo.scrollSpeed);
                            GameModifierManager.LoadModifiersFromString(_lastSongInfo.gamemodifiers);
                            ClickPlay(_lastTrackData);
                        }
                        else
                        {

                            Plugin.LogInfo("Do not own the song " + _lastSongInfo.trackRef);
                            TootTallyNotifManager.DisplayNotif($"Do not own the song #{_lastSongInfo.trackRef}");
                        }
                    else
                        TootTallyNotifManager.DisplayNotif($"No SongInfo from host.");
                else
                    TootTallyNotifManager.DisplayNotif($"Waiting for host to start a song.");

            }

            //Yoinked from DNSpy~ish: Token: 0x0600041F RID: 1055 RVA: 0x0003CFAC File Offset: 0x0003B1AC
            private static void ClickPlay(TromboneTrack track)
            {
                _levelSelectControllerInstance.back_clicked = true;
                _levelSelectControllerInstance.bgmus.Stop();
                _levelSelectControllerInstance.clipPlayer.cancelCrossfades();
                _levelSelectControllerInstance.doSfx(_levelSelectControllerInstance.sfx_musend);
                LeanTween.moveX(_levelSelectControllerInstance.playbtnobj, 640f, 0.6f).setEaseInQuart();
                GlobalVariables.chosen_track = track.trackref;
                GlobalVariables.chosen_track_data = TrackLookup.toTrackData(track);
                _levelSelectControllerInstance.fadeOut("loader", 0.65f);
            }


            private static void ResumeSong()
            {
                _gameControllerInstance.pausecontroller.clickResume();
                SpectatingOverlay.HidePauseText();
            }

            //Yoinked from DNSpy Token: 0x06000276 RID: 630 RVA: 0x000270A8 File Offset: 0x000252A8
            private static void PauseSong()
            {
                if (!_gameControllerInstance.quitting && !_gameControllerInstance.level_finished && _gameControllerInstance.pausecontroller.done_animating && !_gameControllerInstance.freeplay)
                {
                    _isTooting = false;
                    _gameControllerInstance.notebuttonpressed = false;
                    _gameControllerInstance.musictrack.Pause();
                    _gameControllerInstance.sfxrefs.backfromfreeplay.Play();
                    _gameControllerInstance.puppet_humanc.shaking = false;
                    _gameControllerInstance.puppet_humanc.stopParticleEffects();
                    _gameControllerInstance.puppet_humanc.playCameraRotationTween(false);
                    _gameControllerInstance.paused = true;
                    _gameControllerInstance.quitting = true;
                    _gameControllerInstance.pausecanvas.SetActive(true);
                    _gameControllerInstance.pausecontroller.showPausePanel();
                    Cursor.visible = true;
                    if (!_gameControllerInstance.track_is_pausable)
                    {
                        _gameControllerInstance.curtainc.closeCurtain(false);
                    }
                }
            }

            public static void QuitSong()
            {
                _gameControllerInstance.quitting = true;
                ClearSpectatingData();
                _gameControllerInstance.pauseQuitLevel();
                SpectatingOverlay.HidePauseText();
                SpectatingOverlay.HideMarquee();
                SpectatingOverlay.HideStopSpectatingButton();
                SpectatingOverlay.HideViewerIcon();
            }

            private static void RestartSong()
            {
                ClearSpectatingData();
                _waitingToSync = IsSpectating;
                _gameControllerInstance.quitting = true;
                _gameControllerInstance.pauseRetryLevel();
                SpectatingOverlay.HidePauseText();
                SpectatingOverlay.HideMarquee();
                SpectatingOverlay.HideStopSpectatingButton();
                SpectatingOverlay.HideViewerIcon();
            }

            [HarmonyPatch(typeof(PauseCanvasController), nameof(PauseCanvasController.showPausePanel))]
            [HarmonyPrefix]
            public static void OnResumeSetUserStatus(PauseCanvasController __instance)
            {
                if (IsHosting)
                    SetCurrentUserState(UserState.Paused);


                if (Input.GetKeyDown(KeyCode.Escape) && IsSpectating)
                {

                    __instance.gc.quitting = true;
                    __instance.gc.pauseQuitLevel();
                    StopAllSpectator();
                    TootTallyNotifManager.DisplayNotif("Stopped spectating.");
                }
                else if (IsSpectating)
                {
                    __instance.panelobj.SetActive(false);
                    SpectatingOverlay.ShowPauseText();
                }
            }

            [HarmonyPatch(typeof(PauseCanvasController), nameof(PauseCanvasController.clickResume))]
            [HarmonyPostfix]
            public static void OnPauseSetUserStatus()
            {
                if (IsHosting)
                    SetCurrentUserState(UserState.Playing);

            }

            [HarmonyPatch(typeof(GameController), nameof(GameController.pauseQuitLevel))]
            [HarmonyPostfix]
            public static void OnGameControllerUpdate()
            {
                if (IsHosting)
                    SetCurrentUserState(UserState.Quitting);
                else
                    SpectatingOverlay.SetCurrentUserState(UserState.Quitting);
            }

            [HarmonyPatch(typeof(GameController), nameof(GameController.getScoreAverage))]
            [HarmonyPrefix]
            public static void OnGetScoreAveragePrefixSetCurrentNote(GameController __instance)
            {
                if (IsHosting)
                {
                    hostedSpectatingSystem.SendNoteData(__instance.rainbowcontroller.champmode, __instance.multiplier, __instance.currentnoteindex,
                        __instance.notescoreaverage, __instance.released_button_between_notes, __instance.totalscore, __instance.currenthealth, __instance.highestcombo_level);
                }
                else if (IsSpectating)
                {
                    if (_noteData.Count > 0 && _noteData.Last().noteID > __instance.currentnoteindex)
                        _currentNoteData = _noteData.Find(x => x.noteID == __instance.currentnoteindex);
                    if (_currentNoteData.noteID != -1)
                    {
                        __instance.rainbowcontroller.champmode = _currentNoteData.champMode;
                        __instance.multiplier = _currentNoteData.multiplier;
                        __instance.notescoreaverage = (float)_currentNoteData.noteScoreAverage;
                        __instance.released_button_between_notes = _currentNoteData.releasedButtonBetweenNotes;
                        if (__instance.currentscore < 0)
                            __instance.currentscore = _currentNoteData.totalScore;
                        __instance.totalscore = _currentNoteData.totalScore;
                        __instance.currenthealth = _currentNoteData.health;
                        __instance.highestcombo_level = _currentNoteData.highestCombo;
                        _currentNoteData.noteID = -1;
                    }
                }
            }

            private static float _elapsedTime;
            private static bool _isQuickRestarting;
            private static readonly float _targetFramerate = Application.targetFrameRate > 60 || Application.targetFrameRate < 1 ? 60 : Application.targetFrameRate;

            [HarmonyPatch(typeof(GameController), nameof(GameController.Update))]
            [HarmonyPostfix]
            public static void OnUpdatePlaybackSpectatingData(GameController __instance)
            {
                if (IsSpectating)
                {
                    if (!__instance.quitting && !__instance.retrying && (_currentSpecState == UserState.SelectingSong || _lastSongInfo.trackRef == "" || _currentSongInfo.trackRef == "" || _lastSongInfo.trackRef != _currentSongInfo.trackRef))
                        QuitSong();
                    if (!_waitingToSync && !__instance.paused && !__instance.quitting && !__instance.retrying)
                    {
                        _specTracktime += Time.deltaTime * TootTallyGlobalVariables.gameSpeedMultiplier;

                        if (Math.Abs(__instance.musictrack.time - _specTracktime) > .08f)
                        {
                            Plugin.LogInfo("Resynced track time...");
                            _specTracktime = __instance.musictrack.time;
                        }

                        PlaybackSpectatingData(__instance, _specTracktime);
                    }
                    else if (_waitingToSync && __instance.curtainc.doneanimating && !ShouldWaitForSync(out _waitingToSync))
                    {
                        TootTallyNotifManager.DisplayNotif("Finished syncing with host.");
                        __instance.startSong(false);
                    }
                }
                else if (IsHosting && !__instance.paused && !__instance.quitting && !__instance.retrying)
                {
                    _elapsedTime += Time.deltaTime;
                    if (_elapsedTime >= 1f / _targetFramerate)
                    {
                        _elapsedTime = 0f;
                        hostedSpectatingSystem.SendFrameData(__instance.musictrack.time - (GlobalVariables.localsettings.latencyadjust / 1000f), __instance.noteholderr.anchoredPosition.x, __instance.pointer.transform.localPosition.y);
                    }
                }
                else if (IsHosting && __instance.restarttimer > .4f && !_isQuickRestarting)
                {
                    _isQuickRestarting = true;
                    SetCurrentUserState(UserState.Restarting);
                }

            }


            [HarmonyPatch(typeof(GameController), nameof(GameController.pauseRetryLevel))]
            [HarmonyPostfix]
            public static void OnRetryingSetUserStatus()
            {
                if (IsHosting)
                    SetCurrentUserState(UserState.Restarting);
            }

            private static SocketSongInfo _lastHostSongInfo;

            [HarmonyPatch(typeof(LevelSelectController), nameof(LevelSelectController.clickPlay))]
            [HarmonyPostfix]
            public static void OnLevelSelectControllerClickPlaySendToSocket(LevelSelectController __instance)
            {
                _lastHostSongInfo = new SocketSongInfo
                {
                    trackRef = __instance.alltrackslist[__instance.songindex].trackref,
                    songID = 0,
                    gameSpeed = TootTallyGlobalVariables.gameSpeedMultiplier,
                    scrollSpeed = GlobalVariables.gamescrollspeed,
                    gamemodifiers = GameModifierManager.GetModifiersString()
                };

                if (!IsHosting && !IsSpectating && Plugin.Instance.AllowSpectate.Value && TootTallyUser.userInfo.id != 0)
                    CreateUniqueSpectatingConnection(TootTallyUser.userInfo.id, TootTallyUser.userInfo.username); //Remake Hosting connection just in case it wasnt reopened correctly

                if (IsHosting)
                {
                    hostedSpectatingSystem.SendSongInfoToSocket(_lastHostSongInfo);
                    hostedSpectatingSystem.SendUserStateToSocket(UserState.GettingReady);
                }

                SpectatingOverlay.HideViewerIcon();
            }

            [HarmonyPatch(typeof(LevelSelectController), nameof(LevelSelectController.clickPlay))]
            [HarmonyPrefix]
            public static bool OnLevelSelectControllerClickPlayOverwriteIfSpectating()
            {
                if (IsSpectating)
                {
                    if (_spectatingStarting) return true;

                    TryStartSong();
                    return false;
                }
                return true;
            }


            [HarmonyPatch(typeof(LevelSelectController), nameof(LevelSelectController.clickBack))]
            [HarmonyPostfix]
            public static void OnBackButtonClick()
            {
                SpectatingOverlay.HideStopSpectatingButton();
                SpectatingOverlay.HideViewerIcon();
                _levelSelectControllerInstance = null;
                if (IsHosting)
                    SetCurrentUserState(UserState.None);
            }

            private static UserState _currentHostState;
            private static UserState _lastHostState;

            private static void SetCurrentUserState(UserState userState)
            {
                _lastHostState = _currentHostState;
                _currentHostState = userState;
                hostedSpectatingSystem.SendUserStateToSocket(userState);
                Plugin.LogInfo($"Current state changed from {_lastHostState} to {_currentHostState}");
                SpectatingOverlay.SetCurrentUserState(userState);
            }


            [HarmonyPatch(typeof(PointSceneController), nameof(PointSceneController.doCoins))]
            [HarmonyPostfix]
            public static void ReplayIndicator(PointSceneController __instance)
            {
                if (!IsSpectating) return; // Replay not running, an actual play happened
                __instance.tootstext.text = "Spectating Done";
            }

            public static void SendCurrentUserState() => hostedSpectatingSystem?.SendUserStateToSocket(_currentHostState);
        }
        #endregion
    }
}