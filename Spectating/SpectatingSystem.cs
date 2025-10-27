using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.EnterpriseServices;
using TootTallyAccounts;
using TootTallyCore.Utils.TootTallyGlobals;
using TootTallyCore.Utils.TootTallyNotifs;
using TootTallyWebsocketLibs;
using WebSocketSharp;
using static TootTallySpectator.SpectatingManager;

namespace TootTallySpectator
{
    public class SpectatingSystem : WebsocketManager
    {
        private ConcurrentQueue<SocketFrameData> _receivedFrameDataQueue;
        private ConcurrentQueue<SocketTootData> _receivedTootDataQueue;
        private ConcurrentQueue<SocketNoteData> _receivedNoteDataQueue;
        private ConcurrentQueue<SocketSongInfo> _receivedSongInfoQueue;
        private ConcurrentQueue<SocketUserState> _receivedUserStateQueue;
        private ConcurrentQueue<SocketSpectatorInfo> _receivedSpecInfoQueue;

        public Action<SocketFrameData> OnSocketFrameDataReceived;
        public Action<SocketTootData> OnSocketTootDataReceived;
        public Action<SocketNoteData> OnSocketNoteDataReceived;
        public Action<SocketUserState> OnSocketUserStateReceived;
        public Action<SocketSongInfo> OnSocketSongInfoReceived;
        public Action<SocketSpectatorInfo> OnSocketSpecInfoReceived;

        public Action<SpectatingSystem> OnWebSocketOpenCallback;

        public string GetSpectatorUserId => _id;
        public string spectatorName;

        public SpectatingSystem(int id, string name) : base(id.ToString(), "wss://spec.toottally.com:443/spec/", "1.3.0")
        {
            spectatorName = name;
            _receivedFrameDataQueue = new ConcurrentQueue<SocketFrameData>();
            _receivedTootDataQueue = new ConcurrentQueue<SocketTootData>();
            _receivedNoteDataQueue = new ConcurrentQueue<SocketNoteData>();
            _receivedSongInfoQueue = new ConcurrentQueue<SocketSongInfo>();
            _receivedUserStateQueue = new ConcurrentQueue<SocketUserState>();
            _receivedSpecInfoQueue = new ConcurrentQueue<SocketSpectatorInfo>();

            ConnectionPending = true;
            ConnectToWebSocketServer(_url + id, TootTallyAccounts.Plugin.GetAPIKey, id == TootTallyUser.userInfo.id);
        }

        public void SendSongInfoToSocket(string trackRef, int id, float gameSpeed, float scrollSpeed, string gamemodifiers)
        {
            var json = JsonConvert.SerializeObject(new SocketSongInfo() { trackRef = trackRef, songID = id, gameSpeed = gameSpeed, scrollSpeed = scrollSpeed, gamemodifiers = gamemodifiers });
            SendToSocket(json);
        }

        public void SendSongInfoToSocket(SocketSongInfo songInfo)
        {
            SendToSocket(JsonConvert.SerializeObject(songInfo));
        }


        public void SendUserStateToSocket(UserState userState)
        {
            var json = JsonConvert.SerializeObject(new SocketUserState() { userState = (int)userState });
            SendToSocket(json);
        }

        public void SendNoteData(bool champMode, int multiplier, int highestMultiplier, int noteID, double noteScoreAverage, bool releasedButtonBetweenNotes, int totalScore, float health, int highestCombo)
        {
            var socketNoteData = new SocketNoteData()
            {
                champMode = champMode,
                multiplier = multiplier,
                highestMultiplier = highestMultiplier,
                noteID = noteID,
                noteScoreAverage = noteScoreAverage,
                releasedButtonBetweenNotes = releasedButtonBetweenNotes,
                totalScore = totalScore,
                health = health,
                highestCombo = highestCombo
            };
            var json = JsonConvert.SerializeObject(socketNoteData);
            SendToSocket(json);
        }

        public void SendNoteData(SocketNoteData data)
        {
            var json = JsonConvert.SerializeObject(data);
            SendToSocket(json);
        }

        public void SendFrameData(float time, float noteHolder, float pointerPosition)
        {
            var socketFrameData = new SocketFrameData()
            {
                time = time,
                noteHolder = noteHolder,
                pointerPosition = pointerPosition
            };
            var json = JsonConvert.SerializeObject(socketFrameData);
            SendToSocket(json);
        }

        public void SendTootData(float time, float noteHolder, bool isTooting)
        {
            var socketTootData = new SocketTootData()
            {
                time = time,
                noteHolder = noteHolder,
                isTooting = isTooting
            };
            var json = JsonConvert.SerializeObject(socketTootData);
            SendToSocket(json);
        }


        protected override void OnDataReceived(object sender, MessageEventArgs e)
        {
            ISocketMessage socketMessage;
            if (e.IsText)
            {
                if (Plugin.Instance.EnableDebugLogs.Value)
                    Plugin.LogInfo($"Data Received: {e.Data}");
                try
                {
                    socketMessage = JsonConvert.DeserializeObject<ISocketMessage>(e.Data, _dataConverter);
                }
                catch (Exception)
                {
                    Plugin.LogInfo("Couldn't parse to data: " + e.Data);
                    return;
                }
                if (!IsHost)
                {
                    if (socketMessage is SocketSongInfo info)
                        _receivedSongInfoQueue.Enqueue(info);
                    else if (socketMessage is SocketFrameData frame)
                        _receivedFrameDataQueue.Enqueue(frame);
                    else if (socketMessage is SocketTootData toot)
                        _receivedTootDataQueue.Enqueue(toot);
                    else if (socketMessage is SocketUserState state)
                        _receivedUserStateQueue.Enqueue(state);
                    else if (socketMessage is SocketNoteData note)
                        _receivedNoteDataQueue.Enqueue(note);
                }

                if (socketMessage is SocketSpectatorInfo spec)
                {
                    Plugin.LogInfo(e.Data);
                    _receivedSpecInfoQueue.Enqueue(spec);
                }
                //if end up here, nothing was found
            }
        }


        public void UpdateStacks()
        {
            if (ConnectionPending) return;

            if (OnSocketFrameDataReceived != null && _receivedFrameDataQueue.TryDequeue(out SocketFrameData frameData))
                OnSocketFrameDataReceived.Invoke(frameData);

            if (OnSocketTootDataReceived != null && _receivedTootDataQueue.TryDequeue(out SocketTootData tootData))
                OnSocketTootDataReceived.Invoke(tootData);

            if (OnSocketSongInfoReceived != null && _receivedSongInfoQueue.TryDequeue(out SocketSongInfo songInfo))
                OnSocketSongInfoReceived.Invoke(songInfo);

            if (OnSocketUserStateReceived != null && _receivedUserStateQueue.TryDequeue(out SocketUserState userState))
                OnSocketUserStateReceived.Invoke(userState);

            if (OnSocketNoteDataReceived != null && _receivedNoteDataQueue.TryDequeue(out SocketNoteData noteData))
                OnSocketNoteDataReceived.Invoke(noteData);

            if (OnSocketSpecInfoReceived != null && _receivedSpecInfoQueue.TryDequeue(out SocketSpectatorInfo specInfo))
                OnSocketSpecInfoReceived.Invoke(specInfo);

        }

        protected override void OnWebSocketOpen(object sender, EventArgs e)
        {
            TootTallyNotifManager.DisplayNotif($"Connected to spectating server.");
            OnWebSocketOpenCallback?.Invoke(this);
            base.OnWebSocketOpen(sender, e);
        }

        protected override void OnWebSocketClose(object sender, CloseEventArgs e)
        {
            if (!IsHost)
                TootTallyGlobalVariables.isSpectating = false;
            base.OnWebSocketClose(sender, e);
        }

        public void Disconnect()
        {
            if (!IsHost)
            {
                TootTallyGlobalVariables.isSpectating = false;
                TootTallyNotifManager.DisplayNotif($"Disconnected from Spectating server.");
            }
            if (IsConnected)
                CloseWebsocket();
        }

        public void CancelConnection()
        {
            CloseWebsocket();
        }

        public void RemoveFromManager()
        {
            RemoveSpectator(this);
        }

    }
}
