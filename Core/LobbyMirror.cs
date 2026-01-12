using BeatSaber.AvatarCore;
using HarmonyLib;
using IPA.Utilities;
using MultiplayerMirror.Core.Helpers;
using MultiplayerMirror.Core.Scripts;
using SiraUtil.Affinity;
using System;
using System.Collections.Generic;
using UnityEngine;
using Zenject;

namespace MultiplayerMirror.Core
{
    public class LobbyMirror : IInitializable, IDisposable, IAffinity, ITickable
    {
        private static readonly Vector3 MirrorSpawnPos = new(0.0f, 0.0f, 3.0f);
        private static readonly Quaternion MirrorSpawnRot = new(0f, 180f, 0f, 0f);

        [Inject] private readonly PluginConfig _config = null!;
        [Inject] private readonly MultiplayerLobbyAvatarManager _lobbyAvatarManager = null!;

        private IBeatSaberConnectedPlayer? _selfPlayer;
        private IBeatSaberConnectedPlayer? _mockPlayer;
        private MultiplayerLobbyAvatarController? _lobbyAvatarController;
        private AvatarController? _newAvatarController;
        private MirrorAvatarPoseDataProvider? _poseDataProvider;
        private bool _pendingAvatarLoad;

        public void Initialize()
        {
            _config.ChangeEvent += HandleConfigChange;
        }

        public void Dispose()
        {
            _config.ChangeEvent -= HandleConfigChange;
            DestroyMirrorAvatarIfActive();
        }

        private void HandleConfigChange(object sender, EventArgs e)
        {
            var hasAvatar = _lobbyAvatarController != null;
            if (_config.EnableLobbyMirror)
                if (hasAvatar)
                    ApplyInvertAndSwap();
                else
                    CreateMirrorAvatarIfPossible();
            else
                DestroyMirrorAvatarIfActive();
        }

        #region Patches
        [AffinityPatch(typeof(MultiplayerLobbyAvatarManager), "AddPlayer")]
        [AffinityPostfix]
        private void HandleLobbyAvatarAdded(IBeatSaberConnectedPlayer connectedPlayer)
        {
            if (connectedPlayer.isMe)
            {
                // Local player avatar spawned
                _selfPlayer = connectedPlayer;
                CreateMirrorAvatarIfPossible();
                return;
            }
            if (connectedPlayer == _mockPlayer)
            {
                // Mirror mock player avatar spawned (triggered by CreateMirrorAvatarIfPossible)
                HandleMirrorAvatarCreated();
                return;
            }
        }
        #endregion

        #region Avatar management
        /// <summary>
        /// Creates and spawns a mock player avatar in the lobby, as a copy of the local player.
        /// Will only complete if lobby avatars are enabled in config and player data is available.
        /// </summary>
        private bool CreateMirrorAvatarIfPossible()
        {
            if (!_config.EnableLobbyMirror)
                // Lobby mirror disabled in config
                return false;
            if (_selfPlayer is null)
                // Player data not yet available
                return false;
            DestroyMirrorAvatarIfActive();
            // We'll create a mock player that is visually identical to the current player
            _mockPlayer = CreateMockPlayer(_selfPlayer);
            // Next, we'll ask the lobby avatar manager to spawn its avatar
            // This will run do everything including playing the spawn animation
            // Use reflection to invoke the protected/private AddPlayer method
            var addPlayerMethod = AccessTools.Method(typeof(MultiplayerLobbyAvatarManager), "AddPlayer");
            addPlayerMethod.Invoke(_lobbyAvatarManager, new object[] { _mockPlayer });
            // Once complete our HandleLobbyAvatarAdded() postfix will run again for the 2nd step
            return true;
        }

        /// <summary>
        /// Performs finishing touches on the spawned mirror avatar.
        /// </summary>
        private void HandleMirrorAvatarCreated()
        {
            if (_selfPlayer is null || _mockPlayer is null)
                return;
            _lobbyAvatarController = TryGetAvatarController(_mockPlayer.userId);
            if (_lobbyAvatarController is null)
                return;
            var mockTransform = _lobbyAvatarController.gameObject.transform;
            // Tweak position and rotation so it faces the local player
            _lobbyAvatarController.ShowSpawnAnimation(MirrorSpawnPos, MirrorSpawnRot);
            // Disable name tag (to avoid timing issues with spawn coroutine, just disable all its children)
            foreach (Transform tfChild in mockTransform.Find("AvatarCaption"))
                tfChild.gameObject.SetActive(false);
            // Connect base avatar pose controller to the local player's
            var poseController = _lobbyAvatarController.gameObject.GetComponent<MultiplayerAvatarPoseController>();
            poseController.connectedPlayer = _selfPlayer;
            poseController.enabled = true; // pose controller self disables on init when player is not set // TODO this may do nothing, check
            // Replace pose data provider - BeatAvatar uses this, and we can do mirror magic through it
            _newAvatarController = _lobbyAvatarController.gameObject.GetComponent<AvatarController>();
            // Use reflection to access the private field _poseDataProvider
            var poseDataProviderField = AccessTools.Field(typeof(AvatarController), "_poseDataProvider");
            var currentProvider = poseDataProviderField.GetValue(_newAvatarController);
            if (currentProvider is ConnectedPlayerAvatarPoseDataProvider poseProvider)
            {
                _poseDataProvider = new MirrorAvatarPoseDataProvider(_selfPlayer, poseProvider);
                // Replace SetField with reflection SetValue
                poseDataProviderField.SetValue(_newAvatarController, _poseDataProvider);
            }
            // The underlying avatar is probably not loaded; but just in case, ensure it uses our pose provider
            // Use reflection for 'avatar' assuming it's a private field "_avatar"
            var avatarField = AccessTools.Field(typeof(AvatarController), "_avatar");
            var avatarObj = avatarField.GetValue(_newAvatarController);
            if (avatarObj != null)
            {
                // Assuming SetPoseDataProvider is accessible; if not, use AccessTools.Method
                var setPoseMethod = AccessTools.Method(avatarObj.GetType(), "SetPoseDataProvider");
                setPoseMethod.Invoke(avatarObj, new object[] { _poseDataProvider });
            }
            else
                _pendingAvatarLoad = true;
            ApplyInvertAndSwap();
        }

        private void ApplyInvertAndSwap()
        {
            if (_poseDataProvider is not null)
                _poseDataProvider.EnableMirror = !_config.InvertMirror;

            if (_newAvatarController != null)
            {
                // Use reflection for avatar again
                var avatarField = AccessTools.Field(typeof(AvatarController), "_avatar");
                var avatarObj = avatarField.GetValue(_newAvatarController);
                if (avatarObj != null && avatarObj is Component avatarComponent)  // Safe cast to access gameObject
                    HandSwapper.ApplySwap(avatarComponent.gameObject, !_config.InvertMirror);
            }
        }

        private void DestroyMirrorAvatarIfActive()
        {
            if (_mockPlayer is null)
                return;
            // Use reflection to invoke the protected/private RemovePlayer method
            var removePlayerMethod = AccessTools.Method(typeof(MultiplayerLobbyAvatarManager), "RemovePlayer");
            removePlayerMethod.Invoke(_lobbyAvatarManager, new object[] { _mockPlayer });
            _mockPlayer = null;
            _lobbyAvatarController = null;
            _newAvatarController = null;
            _poseDataProvider = null;
            _pendingAvatarLoad = false;
        }
        #endregion

        #region Utils
        private MultiplayerLobbyAvatarController? TryGetAvatarController(string userId)
        {
            var playerIdToAvatarMap =
                _lobbyAvatarManager
                    .GetField<Dictionary<string, MultiplayerLobbyAvatarController>, MultiplayerLobbyAvatarManager>(
                        "_playerIdToAvatarMap");
            if (playerIdToAvatarMap != null && playerIdToAvatarMap.TryGetValue(userId, out var avatarController))
                return avatarController;
            return null;
        }

        private static IBeatSaberConnectedPlayer CreateMockPlayer(IBeatSaberConnectedPlayer basePlayer)
        {
            var mockPlayer = new MockPlayer(new MockPlayerSettings()
            {
                userId = $"Mirror#{basePlayer.userId}",
                userName = $"Mirror#{basePlayer.userName}",
                sortIndex = basePlayer.sortIndex
            }, false);
            mockPlayer.multiplayerAvatarsData = basePlayer.multiplayerAvatarsData;
            // Use reflection to set isConnected if the setter is inaccessible
            var isConnectedProp = AccessTools.Property(typeof(MockPlayer), "isConnected");
            if (isConnectedProp != null)
                isConnectedProp.SetValue(mockPlayer, false);
            return mockPlayer;
        }
        #endregion

        public void Tick()
        {
            if (_poseDataProvider == null || _newAvatarController == null)
                return;
            if (_pendingAvatarLoad)
            {
                // Use reflection for avatar
                var avatarField = AccessTools.Field(typeof(AvatarController), "_avatar");
                var avatarObj = avatarField.GetValue(_newAvatarController);
                if (avatarObj != null)
                {
                    var setPoseMethod = AccessTools.Method(avatarObj.GetType(), "SetPoseDataProvider");
                    setPoseMethod.Invoke(avatarObj, new object[] { _poseDataProvider });
                    ApplyInvertAndSwap();
                    _pendingAvatarLoad = false;
                }
            }

            _poseDataProvider.Tick();
        }
    }
}