using System.Runtime.InteropServices;
using DailyRoutines.Common.KamiToolKit.Addons;
using DailyRoutines.Common.Module.Abstractions;
using DailyRoutines.Common.Module.Enums;
using DailyRoutines.Common.Module.Models;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Graphics.Render;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FFXIVClientStructs.Havok.Animation.Playback.Control.Default;
using KamiToolKit.Enums;
using KamiToolKit.Nodes;
using OmenTools.Interop.Game.Lumina;

namespace DailyRoutines.ModulesPublic.Interface;

public unsafe class PortraitAnimationTimeEditor : ModuleBase
{
    public override ModuleInfo Info => new()
    {
        Title       = Lang.Get("PortraitAnimationTimeEditorTitle"),
        Description = Lang.Get("PortraitAnimationTimeEditorDescription"),
        Author      = ["Yarukon"],
        Category    = ModuleCategory.Interface
    };

    private int   frameCount;
    private float currentFrame;

    private PortraitAnimationTimeEditorAddon? addon;

    protected override void Init() =>
        addon ??= new(this)
        {
            InternalName          = "DRPortraitAnimationTimeEditor",
            Title                 = Lang.Get("PortraitAnimationTimeEditor-AddonTitle"),
            Size                  = new(300f, 136f),
            RememberClosePosition = false
        };

    protected override void Uninit()
    {
        addon?.Dispose();
        addon = null;
    }

    private void SetCurrentFrame
    (
        float frame
    )
    {
        currentFrame = Math.Clamp(frame, 0f, frameCount);
        UpdatePortraitCurrentFrame(currentFrame);
        addon?.SetPlaybackState(true);
    }

    private bool UpdateAnimationState()
    {
        var chara     = PortraitChara;
        var animation = GetAnimationControl(chara);

        if (chara == null || animation == null || CharaView == null)
        {
            frameCount   = 0;
            currentFrame = 0;
            return false;
        }

        var baseTimeline = chara->Timeline.TimelineSequencer.GetSchedulerTimeline(0);

        if (baseTimeline                                              == null ||
            animation->hkaAnimationControl.Binding.ptr                == null ||
            animation->hkaAnimationControl.Binding.ptr->Animation.ptr == null)
        {
            frameCount   = 0;
            currentFrame = 0;
            return false;
        }

        var duration = animation->hkaAnimationControl.Binding.ptr->Animation.ptr->Duration - 0.5f;
        frameCount   = Math.Max(0, (int)Math.Round(30f * duration));
        currentFrame = Math.Clamp(CharaView->GetAnimationTime(), 0f, frameCount);
        return true;
    }

    private static void UpdatePortraitCurrentFrame
    (
        float frame
    )
    {
        var chara = PortraitChara;
        if (chara == null || CharaView == null || EditorState == null || BannerEditor == null)
            return;

        var baseTimeline = chara->Timeline.TimelineSequencer.GetSchedulerTimeline(0);
        if (baseTimeline == null)
            return;

        var delta = frame - baseTimeline->TimelineController.CurrentTimestamp;
        if (delta < 0)
            CharaView->SetPoseTimed(chara->Timeline.BannerTimelineRowId, frame);
        else
            baseTimeline->UpdateBanner(delta);

        CharaView->ToggleAnimationPlayback(true);
        ((AddonBannerEditor*)BannerEditor)->PlayAnimationCheckbox->AtkComponentButton.IsChecked = false;

        if (!EditorState->HasDataChanged)
            EditorState->SetHasChanged(true);
    }

    private static hkaDefaultAnimationControl* GetAnimationControl
    (
        Character* charaActor
    )
    {
        if (charaActor == null) return null;

        var actor = (Actor*)charaActor;
        if (actor->Model                                                                                      == null ||
            actor->Model->Skeleton                                                                            == null ||
            actor->Model->Skeleton->PartialSkeletons                                                          == null ||
            actor->Model->Skeleton->PartialSkeletons->GetHavokAnimatedSkeleton(0)                             == null ||
            actor->Model->Skeleton->PartialSkeletons->GetHavokAnimatedSkeleton(0)->AnimationControls.Length   == 0    ||
            actor->Model->Skeleton->PartialSkeletons->GetHavokAnimatedSkeleton(0)->AnimationControls[0].Value == null)
            return null;

        return actor->Model->Skeleton->PartialSkeletons->GetHavokAnimatedSkeleton(0)->AnimationControls[0];
    }

    private sealed class PortraitAnimationTimeEditorAddon
    (
        PortraitAnimationTimeEditor module
    ) : AttachedAddon("BannerEditor")
    {
        private const float BUTTON_SIZE    = 28f;
        private const float BUTTON_SPACING = 4f;
        private const float CONTROL_WIDTH  = (3f * BUTTON_SIZE) + (2f * BUTTON_SPACING);

        private HorizontalListNode controlRow    = null!;
        private TextNode           frameLabel    = null!;
        private FloatSliderNode    frameSlider   = null!;
        private CircleButtonNode   ControlButton = null!;

        private CircleButtonNode[] controlButtons = [];
        private bool               isSyncing;
        private bool?              pendingPlaybackState;

        protected override AttachedAddonPosition AttachPosition =>
            AttachedAddonPosition.RightTop;

        protected override void OnSetup
        (
            AtkUnitBase*   addon,
            Span<AtkValue> atkValues
        )
        {
            if (WindowNode is WindowNode windowNode)
                windowNode.CloseButtonNode.IsVisible = false;

            var verticalList = new VerticalListNode
            {
                FitContents = true,
                Position    = ContentStartPosition
            };
            verticalList.AttachNode(this);

            controlRow = new()
            {
                Size             = ContentSize with { Y = 28 },
                FirstItemSpacing = (ContentSize.X - CONTROL_WIDTH) / 2f,
                ItemSpacing      = BUTTON_SPACING
            };

            controlButtons =
            [
                new CircleButtonNode
                {
                    IsVisible   = true,
                    IsEnabled   = true,
                    Size        = new(BUTTON_SIZE),
                    Icon        = CircleButtonIcon.LeftArrow,
                    TextTooltip = "-1",
                    OnClick = () => module.SetCurrentFrame
                    (
                        module.currentFrame % 1f == 0 ?
                            module.currentFrame                - 1f :
                            MathF.Ceiling(module.currentFrame) - 1f
                    )
                },
                ControlButton = new CircleButtonNode
                {
                    IsVisible   = true,
                    IsEnabled   = true,
                    Size        = new(BUTTON_SIZE),
                    Icon        = CircleButtonIcon.MusicNote,
                    TextTooltip = LuminaWrapper.GetAddonText(4802),
                    OnClick = () =>
                    {
                        var chara   = PortraitChara;
                        var control = GetAnimationControl(chara);
                        if (chara == null || control == null || CharaView == null || BannerEditor == null)
                            return;

                        var actualIsPlaying = control->PlaybackSpeed > 0;
                        var isPlaying       = GetPlaybackState(actualIsPlaying);
                        CharaView->ToggleAnimationPlayback(isPlaying);
                        ((AddonBannerEditor*)BannerEditor)->PlayAnimationCheckbox->AtkComponentButton.IsChecked = false;
                        SetPlaybackState(!isPlaying, true);
                    }
                },
                new CircleButtonNode
                {
                    IsVisible   = true,
                    IsEnabled   = true,
                    Size        = new(BUTTON_SIZE),
                    Icon        = CircleButtonIcon.RightArrow,
                    TextTooltip = "+1",
                    OnClick = () => module.SetCurrentFrame
                    (
                        module.currentFrame % 1f == 0 ?
                            module.currentFrame + 1f :
                            MathF.Ceiling(module.currentFrame)
                    )
                }
            ];
            controlRow.AddNode(controlButtons);
            controlRow.RecalculateLayout();

            verticalList.AddNode(controlRow);

            frameLabel = new TextNode
            {
                AlignmentType = AlignmentType.Center,
                TextFlags     = TextFlags.Edge | TextFlags.AutoAdjustNodeSize,
                String        = "000.00 / 000",
                Size          = new(Size.X - 24f, 28f)
            };

            verticalList.AddNode(frameLabel);

            frameSlider = new FloatSliderNode
            {
                Min  = 0f,
                Max  = 1f,
                Step = 1f,
                Size = new(Size.X + 20f, 28f),
                OnValueChanged = value =>
                {
                    if (!isSyncing)
                        module.SetCurrentFrame(value);
                }
            };
            frameSlider.FloatValueNode.FontSize = 0;

            verticalList.AddNode(frameSlider);
        }

        protected override void OnAttachedAddonUpdate
        (
            AtkUnitBase* addon,
            AtkUnitBase* hostAddon
        )
        {
            var charaResNode = hostAddon->GetNodeById(107);
            if (charaResNode == null)
                return;

            var hasAnimation = module.UpdateAnimationState();
            SyncControls(hasAnimation);
        }

        private void SyncControls
        (
            bool hasAnimation
        )
        {
            var maxFrame = MathF.Max(1f, module.frameCount);

            foreach (var button in controlButtons)
                button.IsEnabled = hasAnimation;

            frameSlider.IsEnabled = hasAnimation;
            var control         = GetAnimationControl(PortraitChara);
            var actualIsPlaying = control != null && control->PlaybackSpeed > 0;
            if (!hasAnimation || (pendingPlaybackState is { } pendingState && pendingState == actualIsPlaying))
                pendingPlaybackState = null;

            var isPlaying = pendingPlaybackState ?? actualIsPlaying;
            UpdatePlaybackButton(isPlaying);

            frameLabel.String = module.frameCount < 100 ?
                                    $"{module.currentFrame:F3} / {module.frameCount}" :
                                    $"{module.currentFrame:F2} / {module.frameCount}";

            isSyncing = true;
            if (MathF.Abs(frameSlider.Max - maxFrame) > 0.001f)
                frameSlider.Max = maxFrame;
            frameSlider.Value = module.currentFrame;
            isSyncing         = false;
        }

        public bool GetPlaybackState
        (
            bool actualIsPlaying
        ) => pendingPlaybackState ?? actualIsPlaying;

        public void SetPlaybackState
        (
            bool isPlaying,
            bool refreshTooltip = false
        )
        {
            pendingPlaybackState = isPlaying;
            UpdatePlaybackButton(isPlaying);

            if (!refreshTooltip)
                return;

            ControlButton.HideTooltip();
            ControlButton.ShowTooltip();
        }

        private void UpdatePlaybackButton
        (
            bool isPlaying
        )
        {
            ControlButton.Icon = isPlaying ?
                                     CircleButtonIcon.WavePulse :
                                     CircleButtonIcon.MusicNote;
            ControlButton.TextTooltip = isPlaying ?
                                            LuminaWrapper.GetAddonText(13910) :
                                            LuminaWrapper.GetAddonText(4802);
        }
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct Actor
    {
        [FieldOffset(256)]
        public ActorModel* Model;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct ActorModel
    {
        [FieldOffset(160)]
        public Skeleton* Skeleton;
    }

    #region 常量

    private static AgentBannerEditorState* EditorState =>
        AgentBannerEditor.Instance()->EditorState;

    private static CharaViewPortrait* CharaView =>
        EditorState != null ?
            EditorState->CharaView :
            null;

    private static Character* PortraitChara =>
        CharaView != null ?
            CharaView->GetCharacter() :
            null;

    #endregion
}
