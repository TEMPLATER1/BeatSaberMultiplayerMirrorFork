using BeatSaber.AvatarCore;
using MultiplayerMirror.Core.Helpers;
using UnityEngine;
using HarmonyLib;  // Added for AccessTools

namespace MultiplayerMirror.Core.Scripts
{
    public class MirrorAvatarPoseController : MonoBehaviour
    {
        public bool EnableMirror { get; set; }
        public bool RestrictPose { get; set; }
        public IBeatSaberConnectedPlayer? LocalPlayer { get; set; }
        public INodePoseSyncStateManager? NodePoseSyncStateManager { get; set; }
        public IAvatarPoseRestriction? AvatarPoseRestriction { get; set; }
        public Transform? LeftSaberTransform { get; set; }
        public Transform? RightSaberTransform { get; set; }
        public Transform? HeadTransform { get; set; }

        public void Init(IBeatSaberConnectedPlayer localPlayer,
            INodePoseSyncStateManager nodePoseSyncStateManager,
            IAvatarPoseRestriction avatarPoseRestriction, Transform leftSaberTransform, Transform rightSaberTransform,
            Transform headTransform)
        {
            EnableMirror = true;
            RestrictPose = true;
            LocalPlayer = localPlayer;
            NodePoseSyncStateManager = nodePoseSyncStateManager;
            AvatarPoseRestriction = avatarPoseRestriction;
            LeftSaberTransform = leftSaberTransform;
            RightSaberTransform = rightSaberTransform;
            HeadTransform = headTransform;
        }

        public void Init(IBeatSaberConnectedPlayer localPlayer, MultiplayerAvatarPoseController baseController)
        {
            // Use reflection to access private fields from baseController
            var nodePoseSyncField = AccessTools.Field(typeof(MultiplayerAvatarPoseController), "_nodePoseSyncStateManager");
            var avatarPoseRestrictionField = AccessTools.Field(typeof(MultiplayerAvatarPoseController), "_avatarPoseRestriction");
            var leftSaberField = AccessTools.Field(typeof(MultiplayerAvatarPoseController), "_leftSaberTransform");
            var rightSaberField = AccessTools.Field(typeof(MultiplayerAvatarPoseController), "_rightSaberTransform");
            var headField = AccessTools.Field(typeof(MultiplayerAvatarPoseController), "_headTransform");

            var nodePoseSyncStateManager = (INodePoseSyncStateManager)nodePoseSyncField.GetValue(baseController);
            var avatarPoseRestriction = (IAvatarPoseRestriction)avatarPoseRestrictionField.GetValue(baseController);
            var leftSaberTransform = (Transform)leftSaberField.GetValue(baseController);
            var rightSaberTransform = (Transform)rightSaberField.GetValue(baseController);
            var headTransform = (Transform)headField.GetValue(baseController);

            Init(localPlayer, nodePoseSyncStateManager, avatarPoseRestriction,
                leftSaberTransform, rightSaberTransform, headTransform);
        }

        public void Update()
        {
            if (NodePoseSyncStateManager == null || LocalPlayer == null)
                // Init() not called yet
                return;
            var localState = NodePoseSyncStateManager.localState;
            if (localState == null)
                return;
            var offsetTime = LocalPlayer.offsetSyncTime;
            var headPose = localState.GetState(NodePoseSyncState.NodePose.Head, offsetTime);
            var leftPose = localState.GetState(NodePoseSyncState.NodePose.LeftController, offsetTime);
            var rightPose = localState.GetState(NodePoseSyncState.NodePose.RightController, offsetTime);
            Vector3 headPos = headPose.position;
            Vector3 leftPos = leftPose.position;
            Vector3 rightPos = rightPose.position;
            Quaternion headRot = headPose.rotation;
            Quaternion leftRot = leftPose.rotation;
            Quaternion rightRot = rightPose.rotation;
            if (EnableMirror)
            {
                headPos = MirrorUtil.MirrorPosition(headPos);
                leftPos = MirrorUtil.MirrorPosition(leftPos);
                rightPos = MirrorUtil.MirrorPosition(rightPos);
                headRot = MirrorUtil.MirrorRotation(headRot);
                leftRot = MirrorUtil.MirrorRotation(leftRot);
                rightRot = MirrorUtil.MirrorRotation(rightRot);
            }
            if (RestrictPose)
            {
                AvatarPoseRestriction?.RestrictPose(headRot, headPos, leftPos, rightPos,
                    out headPos, out leftPos, out rightPos);
            }
            if (LeftSaberTransform != null)
                LeftSaberTransform.SetLocalPositionAndRotation(leftPos, leftRot);
            if (RightSaberTransform != null)
                RightSaberTransform.SetLocalPositionAndRotation(rightPos, rightRot);
            if (HeadTransform != null)
                HeadTransform.SetLocalPositionAndRotation(headPos, headRot);
        }
    }
}