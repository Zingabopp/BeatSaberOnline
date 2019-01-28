﻿using Steamworks;
using UnityEngine.Networking;
using System.Net;
using System.Collections.Generic;
using System;
using BeatSaberOnline.Controllers;
using System.Text;
using BeatSaberOnline.Views.Menus;
using System.Linq;
using UnityEngine;

namespace BeatSaberOnline.Data.Steam
{
    public static class SteamAPI
    {

        public enum ConnectionState
        {
            UNDEFINED,
            CONNECTING,
            CANCELLED,
            CONNECTED,
            FAILED,
            DISCONNECTING,
            DISCONNECTED
        }

        static string userName;
        static ulong userID;
        private static CallResult<LobbyMatchList_t> OnLobbyMatchListCallResult;
        private static CallResult<LobbyCreated_t> OnLobbyCreatedCallResult;

        private static SteamCallbacks callbacks;
        static LobbyInfo _lobbyInfo;
        public static Dictionary<CSteamID, bool> ReadyState = new Dictionary<CSteamID, bool>();
        public static ConnectionState Connection { get; set; } = ConnectionState.UNDEFINED;
        public static Dictionary<ulong, LobbyInfo> LobbyData { get; set; } = new Dictionary<ulong, LobbyInfo>();

        public static void Init()
        {
            UpdateUserInfo();
            OnLobbyMatchListCallResult = CallResult<LobbyMatchList_t>.Create(OnLobbyMatchList);
            OnLobbyCreatedCallResult = CallResult<LobbyCreated_t>.Create(OnLobbyCreated);
            callbacks = new SteamCallbacks();
            _lobbyInfo = new LobbyInfo();


            string[] args = System.Environment.GetCommandLineArgs();
            string input = "";
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "+connect_lobby" && args.Length > i + 1)
                {
                    input = args[i + 1];
                }
            }

            if (!string.IsNullOrEmpty(input))
            {
                ulong lobbyId = Convert.ToUInt64(input);

                if (lobbyId > 0)
                {
                    Logger.Debug($"Game was started with +connect_lobby, lets join it @ {lobbyId}");
                    JoinLobby(new CSteamID(lobbyId));
                }
            }
        }

        public static void UpdateUserInfo()
        {
            if (userID == 0 || userName == null)
            {
                Logger.Debug($"Updating current user info");
                userName = SteamFriends.GetPersonaName();
                userID = SteamUser.GetSteamID().m_SteamID;
            }
        }

        public static string GetUserName()
        {
            return userName;
        }

        public static ulong GetUserID()
        {
            return userID;
        }

        public static CSteamID getLobbyID()
        {
            return _lobbyInfo.LobbyID;
        }

        public static ConnectionState GetConnectionState()
        {
            return Connection;
        }

        public static void SetConnectionState(ConnectionState _connection)
        {
            Connection = _connection;
        }


        public static bool IsLobbyJoinable()
        {
            return _lobbyInfo.Joinable;
        }

        public static int getSlotsOpen()
        {
            return _lobbyInfo.TotalSlots;
        }

        public static void ToggleLobbyJoinable()
        {
            _lobbyInfo.Joinable = !_lobbyInfo.Joinable;
            SteamMatchmaking.SetLobbyJoinable(_lobbyInfo.LobbyID, _lobbyInfo.Joinable);
            SendLobbyInfo(true);
        }
        public static void SetReady()
        {
            Logger.Debug($"Broadcast to our lobby that we are ready");
            Controllers.PlayerController.Instance._playerInfo.Downloading = true;

            if (_lobbyInfo.UsedSlots == 1)
            {
                StartPlaying();
            }
            WaitingMenu.RefreshData(false);
        }

        public static GameplayModifiers GetGameplayModifiers()
        {
            return _lobbyInfo.GameplayModifiers;
        }
        public static void ClearPlayerReady(CSteamID steamId, bool push)
        {
            ReadyState.Remove(steamId);
            if (push) {
                Logger.Debug($"Broadcast to our lobby that our ready status should be cleared");
                Controllers.PlayerController.Instance._playerInfo.Downloading = false;
            }

        }

        public static void StartPlaying()
        {
            _lobbyInfo.Screen = LobbyInfo.SCREEN_TYPE.PLAY_SONG;
            SendLobbyInfo(true);
        }
        public static void StartGame()
        {
            _lobbyInfo.Screen = LobbyInfo.SCREEN_TYPE.IN_GAME;
            SendLobbyInfo(true);
        }

        public static Dictionary<string, bool> getAllPlayerStatusesInLobby()
        {
            Dictionary<string, bool> status = new Dictionary<string, bool>();
            int numMembers = SteamMatchmaking.GetNumLobbyMembers(_lobbyInfo.LobbyID);
            for (int i = 0; i < numMembers; i++)
            {
                CSteamID member = SteamMatchmaking.GetLobbyMemberByIndex(_lobbyInfo.LobbyID, i);
                string name =  SteamFriends.GetFriendPersonaName(member);
                status.Add(name, ReadyState.ContainsKey(member) && ReadyState[member]);
            }
            return status;
        }
        public static void RequestPlay(GameplayModifiers gameplayModifiers)
        {
            if (IsHost())
            {
                try
                {
                    Logger.Debug($"update the current screen to the waiting screen while people download the song");
                    LevelSO song = WaitingMenu.GetInstalledSong();
                    if (song != null)
                    {
                        setLobbyStatus("Loading " + song.songName + " by " + song.songAuthorName);
                    }
                    _lobbyInfo.GameplayModifiers = gameplayModifiers;
                    _lobbyInfo.Screen = LobbyInfo.SCREEN_TYPE.WAITING;
                    SendLobbyInfo(true);
                } catch (Exception e)
                {
                    Logger.Error(e);
                }
            }
        }

        public static string GetSongId()
        {
            return _lobbyInfo.CurrentSongId;
        }
        public static byte GetSongDifficulty()
        {
            return _lobbyInfo.CurrentSongDifficulty;
        }

        public static void SetSong(string songId, string songName)
        {
            _lobbyInfo.CurrentSongId = songId;
            _lobbyInfo.CurrentSongName = songName;
            Logger.Debug($"We want to play {songId} - {songName}");
            SendLobbyInfo(true);
        }

        public static bool IsHost()
        {
            if (_lobbyInfo.LobbyID.m_SteamID == 0) { return true;  }
            bool host = SteamMatchmaking.GetLobbyOwner(_lobbyInfo.LobbyID).m_SteamID == GetUserID();
            if (host) {
                Logger.Debug($"We are the host");
            }
            return host;
        }

        public static void SetDifficulty(byte songDifficulty)
        {
            Logger.Debug($"We want to play on {songDifficulty}");
            _lobbyInfo.CurrentSongDifficulty = songDifficulty;
            SendLobbyInfo(true);
        }

        public static void StopSong()
        {
            Logger.Debug($"Broadcast to the lobby that we are back on the menu");

            _lobbyInfo.Screen = LobbyInfo.SCREEN_TYPE.MENU;
            SendLobbyInfo(true);
            setLobbyStatus("Waiting In Menu");
        }

        public static void ResetScreen()
        {
                Logger.Debug($"Clear the current screen from the lobby");

                _lobbyInfo.Screen = LobbyInfo.SCREEN_TYPE.NONE;
                SendLobbyInfo(true);
        }

        public static int getUserCount()
        {
            return SteamMatchmaking.GetNumLobbyMembers(_lobbyInfo.LobbyID) + 1;
        }
        public static void FinishSong()
        {
            Logger.Debug($"We have finished the song");

            ReadyState.Clear();

            SendLobbyInfo(true);
            setLobbyStatus("Waiting In Menu");

        }

        private static void SendLobbyInfo(bool reqHost = false)
        {
             if (reqHost && !IsHost()) return;
             SteamMatchmaking.SetLobbyData(_lobbyInfo.LobbyID, "LOBBY_INFO", _lobbyInfo.Serialize());
        }
        public static void IncreaseSlots()
        {
            _lobbyInfo.TotalSlots += 1;
            if (_lobbyInfo.TotalSlots > _lobbyInfo.MaxSlots)
            {
                _lobbyInfo.TotalSlots = 2;
            }
            Logger.Debug($"Increasing the lobby slots to {_lobbyInfo.TotalSlots}");

            SendLobbyInfo(true);
            SteamMatchmaking.SetLobbyMemberLimit(_lobbyInfo.LobbyID, _lobbyInfo.TotalSlots);
        }

        public static CGameID GetGameID()
        {
            var fgi = new FriendGameInfo_t();
            SteamFriends.GetFriendGamePlayed(new CSteamID(userID), out fgi);
            return fgi.m_gameID;
        }

        public static void RequestLobbies()
        {
            if (!SteamManager.Initialized)
            {
                Logger.Error("CONNECTION FAILED");
                return;
            }
            Logger.Debug($"Requesting list of all lobbies from steam");

            SteamMatchmaking.AddRequestLobbyListFilterSlotsAvailable(1);
            SteamAPICall_t apiCall = SteamMatchmaking.RequestLobbyList();
            OnLobbyMatchListCallResult.Set(apiCall);
        }

        public static Dictionary<CSteamID, string[]> GetOnlineFriends()
        {
            var friends = new Dictionary<CSteamID, string[]>(); if (!SteamManager.Initialized)
            {
                Logger.Error("CONNECTION FAILED");
                return friends;
            }
            try
            {
                int friendCount = SteamFriends.GetFriendCount(EFriendFlags.k_EFriendFlagImmediate);
                for (int i = 0; i < friendCount; ++i)
                {
                    CSteamID friendSteamId = SteamFriends.GetFriendByIndex(i, EFriendFlags.k_EFriendFlagImmediate);
                    string friendName = SteamFriends.GetFriendPersonaName(friendSteamId);
                    EPersonaState friendState = SteamFriends.GetFriendPersonaState(friendSteamId);
                    if (friendState != EPersonaState.k_EPersonaStateOffline)
                    {
                        var fgi = new FriendGameInfo_t();
                        bool ret = SteamFriends.GetFriendGamePlayed(friendSteamId, out fgi);
                        friends.Add(friendSteamId,  new string[]{ friendName, ""+fgi.m_gameID});
                    }
                }
            } catch (Exception e)
            {
                Logger.Error(e);
            }
            return friends;
        }
        
        public static void OpenInviteScreen()
        {
            if (!SteamManager.Initialized)
            {
                Logger.Error("CONNECTION FAILED");
                return;
            }
            SteamFriends.ActivateGameOverlayInviteDialog(_lobbyInfo.LobbyID);
        }
        public static void PlayerConnected()
        {
            _lobbyInfo.UsedSlots += 1;
            SendLobbyInfo(true);
        }
        public static void PlayerDisconnected()
        {
            _lobbyInfo.UsedSlots -= 1;
            SendLobbyInfo(true);
        }

        public static void OnLobbyMatchList(LobbyMatchList_t pCallback, bool bIOFailure)
        {
            if (!SteamManager.Initialized)
            {
                Logger.Error("CONNECTION FAILED");
                return;
            }
            uint numLobbies = pCallback.m_nLobbiesMatching;
            Logger.Info($"Found {numLobbies} total lobbies");
            LobbyData.Clear();
            OnlineMenu.refreshLobbyList();
            try
            {
                for (int i = 0; i < numLobbies; i++)
                {
                    CSteamID lobbyId = SteamMatchmaking.GetLobbyByIndex(i);
                    if (lobbyId.m_SteamID == _lobbyInfo.LobbyID.m_SteamID) { continue; }
                    LobbyInfo info = new LobbyInfo(SteamMatchmaking.GetLobbyData(lobbyId, "LOBBY_INFO"));

                    SetOtherLobbyData(lobbyId.m_SteamID, info, false);
                    Logger.Info($"{info.HostName} has {info.UsedSlots} users in it and is currently {info.Status}");
                }
            } catch (Exception e)
            {
                Logger.Info(e);
            }

            OnlineMenu.refreshLobbyList();
        }

        public static void CreateLobby()
        {
            if (isLobbyConnected()) { return; }
            Logger.Debug($"Creating a lobby");
            SteamAPICall_t handle = SteamMatchmaking.CreateLobby(Config.Instance.IsPublic ? ELobbyType.k_ELobbyTypePublic : ELobbyType.k_ELobbyTypeFriendsOnly, Config.Instance.MaxLobbySize);
            OnLobbyCreatedCallResult.Set(handle);
        }

        public static void InviteUserToLobby(CSteamID userId)
        {
            if (!SteamManager.Initialized)
            {
                Logger.Error("CONNECTION FAILED");
                return;
            }
            Logger.Debug($"Inviting {userId} to our lobby");

            bool ret = SteamMatchmaking.InviteUserToLobby(_lobbyInfo.LobbyID, userId);
        }

        public static bool isLobbyConnected()
        {
            return SteamManager.Initialized && Connection == ConnectionState.CONNECTED && _lobbyInfo.LobbyID.m_SteamID > 0;
        }
        private static void OnLobbyCreated(LobbyCreated_t pCallback, bool bIOFailure)
        {

            if (!SteamManager.Initialized)
            {
                Logger.Error("CONNECTION FAILED");
                return;
            }
            if (!bIOFailure) {
                _lobbyInfo.LobbyID = new CSteamID(pCallback.m_ulSteamIDLobby);
                _lobbyInfo.HostName = GetUserName();
                Logger.Debug($"Lobby has been created");
                var hostUserId = SteamMatchmaking.GetLobbyOwner(_lobbyInfo.LobbyID);
                var me = SteamUser.GetSteamID();
                Connection = ConnectionState.CONNECTED;
                if (hostUserId.m_SteamID == me.m_SteamID)
                {
                    setLobbyStatus("Waiting In Menu");
                        
                    SendLobbyInfo(true);
                }
            }
        }
        
        public static void JoinLobby(CSteamID lobbyId)
        {
            if (!SteamManager.Initialized)
            {
                Connection = ConnectionState.FAILED;
                Logger.Error("CONNECTION FAILED");
                return;
            }
            if (_lobbyInfo.LobbyID.m_SteamID > 0)
            {

                Logger.Debug($"We are already in another lobby, lets disconnect first");
                Disconnect();
            }
            Connection = ConnectionState.CONNECTING;
            _lobbyInfo.LobbyID = lobbyId;

            Logger.Debug($"Joining a new steam lobby {lobbyId}");
            SteamMatchmaking.JoinLobby(lobbyId);
        }
        
        public static void RequestAvailableLobbies()
        {
            if (!SteamManager.Initialized)
            {
                Logger.Error("CONNECTION FAILED");
                return;
            }
            LobbyData.Clear();
            OnlineMenu.refreshLobbyList();
            int cFriends = SteamFriends.GetFriendCount(EFriendFlags.k_EFriendFlagImmediate);
                for (int i = 0; i < cFriends; i++)
                {
                    FriendGameInfo_t friendGameInfo;
                    CSteamID steamIDFriend = SteamFriends.GetFriendByIndex(i, EFriendFlags.k_EFriendFlagImmediate); SteamFriends.GetFriendGamePlayed(steamIDFriend, out friendGameInfo);

                    if (friendGameInfo.m_gameID == GetGameID() && friendGameInfo.m_steamIDLobby.IsValid())
                    {
                       SteamMatchmaking.RequestLobbyData(friendGameInfo.m_steamIDLobby);
                    }
                }
        }

        public static bool IsMemberInSteamLobby(CSteamID steamUser)
        {
            if (!SteamManager.Initialized)
            {
                Logger.Error("CONNECTION FAILED");
                return false;
            }
            int numMembers = SteamMatchmaking.GetNumLobbyMembers(_lobbyInfo.LobbyID);

                for (int i = 0; i < numMembers; i++)
                {
                    var member = SteamMatchmaking.GetLobbyMemberByIndex(_lobbyInfo.LobbyID, i);

                    if (member.m_SteamID == steamUser.m_SteamID)
                    {
                        return true;
                    }
                }

            return false;
        }

        public static Dictionary<CSteamID, string> GetMembersInLobby()
        {
            Dictionary<CSteamID, string> members = new Dictionary<CSteamID, string>();
            if (!SteamManager.Initialized)
            {
                Logger.Error("CONNECTION FAILED");
                return members;
            }
            int numMembers = SteamMatchmaking.GetNumLobbyMembers(_lobbyInfo.LobbyID);
            for (int i = 0; i < numMembers; i++)
            {
                CSteamID member = SteamMatchmaking.GetLobbyMemberByIndex(_lobbyInfo.LobbyID, i);
                members.Add(member, SteamFriends.GetFriendPersonaName(member));
            }

            return members;
        }

        public static void UpdateLobbyInfo(LobbyInfo info)
        {
            _lobbyInfo = info;
        }
        public static void setLobbyStatus(string value)
        {
            Logger.Debug($"Update lobby status to {value}");
            _lobbyInfo.Status = value;
            SendLobbyInfo(true);
        }
        
        public static void SendPlayerInfo(PlayerInfo playerInfo)
        {
            var message = playerInfo.Serialize().Trim();
            byte[] bytes = Encoding.UTF8.GetBytes(message);
            SendToAllInLobby(bytes);
        }

        public static void SendToAllInLobby(byte[] bytes)
        {
            int numMembers = SteamMatchmaking.GetNumLobbyMembers(_lobbyInfo.LobbyID);
            for (int i = 0; i < numMembers; i++)
            {
                CSteamID member = SteamMatchmaking.GetLobbyMemberByIndex(_lobbyInfo.LobbyID, i);
                if (member.m_SteamID != SteamAPI.GetUserID())
                {
                    SteamNetworking.SendP2PPacket(member, bytes, (uint)bytes.Length, EP2PSend.k_EP2PSendReliable);
                }
            }
        }
        public static void Disconnect()
        {
            try
            {
                Logger.Debug($"Disconnect from current lobby");
                _lobbyInfo.HostName = "";
                SendLobbyInfo(true);
                Connection = ConnectionState.DISCONNECTED;
                _lobbyInfo.LobbyID = new CSteamID(0);
                SteamMatchmaking.LeaveLobby(_lobbyInfo.LobbyID);
                Controllers.PlayerController.Instance.DestroyAvatars();
            } catch (Exception e)
            {
                Logger.Error(e);
            }
        }
        public static void SetOtherLobbyData(ulong lobbyId, LobbyInfo info, bool refresh = true)
        {
            info.UsedSlots = SteamMatchmaking.GetNumLobbyMembers(new CSteamID(lobbyId));
            if (info.UsedSlots > 0 && info.HostName != "")
            {
                LobbyData.Add(lobbyId, info);

                OnlineMenu.refreshLobbyList();
            }
        }
    }
}
