// T14: Free-text input panel for the player when they are the Participating
// speaker in an AI Tavern conversation.
//
// Lives in the `Bridge/` folder (no .asmdef → Assembly-CSharp) so it can
// reference both Jyx2_UIBase / Jyx2_UIManager (game core) and Conversation /
// GameId (AITavern.Runtime asmdef).
//
// Behavior (per Plan §3.10 + §4.7, INV-3.10-6 — mirrors ai-town
// MessageInput.tsx):
//   - Acquires Conversation.IsTyping on Show. If the lock is held by another
//     player, the panel refuses to open and closes itself without invoking
//     submit/cancel callbacks.
//   - Submit hands the text to the caller's onSubmit callback — the caller is
//     responsible for invoking Conversation.AddMessage with the right IClock
//     `now`. AddMessage clears the typing lock, so the panel does NOT call
//     ClearIsTyping on submit.
//   - Cancel calls Conversation.ClearIsTyping directly and hides.
//   - Defensive hide (e.g. external HideAllUI) also releases the lock.
//
// IMPORTANT: this script is one of two halves of T14. The other half is a
// Unity prefab `AITavernPlayerSpeakPanel.prefab` that the user creates in the
// Editor (USER SMOKE HANDOFF per §10.2.1). This task delivers only the script.
using System;
using UnityEngine;
using UnityEngine.UI;
using Jyx2;

namespace Jyx2.AITavern.Bridge
{
    /// <summary>
    /// Free-text input panel for the player when they are the Participating
    /// speaker in an AI Tavern conversation. Acquires Conversation.IsTyping
    /// lock on Show (per INV-3.10-6 — mirrors ai-town MessageInput.tsx).
    /// Refuses to open if another agent holds the lock. Submit → caller drives
    /// AddMessage (which clears the lock). Cancel → ClearIsTyping.
    /// </summary>
    public partial class AITavernPlayerSpeakPanel : Jyx2_UIBase
    {
        [SerializeField] private InputField m_InputField;
        [SerializeField] private Text m_PromptLabel;       // shows the other NPC's last line for context (optional)
        [SerializeField] private Button m_SubmitButton;    // optional — Enter is wired in code
        [SerializeField] private Button m_CancelButton;    // optional — Esc is wired in code

        public override UILayer Layer => UILayer.NormalUI;
        public override bool IsOnly => true;

        // Wiring set per-Show
        Conversation _conv;
        GameId _playerAgentId;
        Action<string> _onSubmit;
        Action _onCancel;
        string _typingUuid;
        bool _lockHeld;

        protected override void OnCreate()
        {
            base.OnCreate();
            if (m_SubmitButton != null) m_SubmitButton.onClick.AddListener(OnSubmitClicked);
            if (m_CancelButton != null) m_CancelButton.onClick.AddListener(OnCancelClicked);
        }

        // Args contract (mirrors ChatUIPanel.OnShowPanel(params object[] args)):
        //   args[0] = Conversation
        //   args[1] = GameId (player's agent id)
        //   args[2] = string (other NPC's last line, may be null)
        //   args[3] = Action<string> onSubmit
        //   args[4] = Action onCancel
        //   args[5] = long now (ms epoch — caller provides so panel uses same clock as FSM)
        protected override void OnShowPanel(params object[] allParams)
        {
            base.OnShowPanel(allParams);

            _conv = (Conversation)allParams[0];
            _playerAgentId = (GameId)allParams[1];
            string lastLine = (string)allParams[2];
            _onSubmit = (Action<string>)allParams[3];
            _onCancel = (Action)allParams[4];
            long now = (long)allParams[5];

            if (m_PromptLabel != null) m_PromptLabel.text = lastLine ?? string.Empty;
            if (m_InputField != null)
            {
                m_InputField.text = string.Empty;
                m_InputField.ActivateInputField();
            }

            // INV-3.10-6: acquire typing lock on Show. If held by another player, refuse.
            try
            {
                _typingUuid = Guid.NewGuid().ToString("N");
                _conv.SetIsTyping(_playerAgentId, _typingUuid, now);
                _lockHeld = true;
            }
            catch (InvalidOperationException e)
            {
                Debug.LogWarning($"[AITavernPlayerSpeakPanel] could not acquire typing lock: {e.Message}");
                _lockHeld = false;
                // Immediately close without invoking submit/cancel callbacks.
                Jyx2_UIManager.Instance.HideUI(nameof(AITavernPlayerSpeakPanel));
            }
        }

        protected override void OnHidePanel()
        {
            base.OnHidePanel();
            // Defensive: if we still hold the lock and the panel is hiding without an
            // explicit submit/cancel (e.g. external HideAllUI), release it.
            if (_lockHeld && _conv != null)
            {
                try { _conv.ClearIsTyping(); } catch { /* swallow — conv may be stopped */ }
                _lockHeld = false;
            }
            _conv = null;
            _onSubmit = null;
            _onCancel = null;
        }

        public override void Update()
        {
            base.Update();
            // Only react when this panel is the top visible UI, so Enter/Esc
            // don't leak through to other panels stacked on top.
            if (_conv == null) return;

            // Enter to submit, Esc to cancel — works whether or not the prefab has buttons.
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                OnSubmitClicked();
            }
            else if (Input.GetKeyDown(KeyCode.Escape))
            {
                OnCancelClicked();
            }
        }

        void OnSubmitClicked()
        {
            if (_conv == null) return;
            string text = m_InputField != null ? (m_InputField.text ?? string.Empty).Trim() : string.Empty;
            if (string.IsNullOrEmpty(text))
            {
                return; // require non-empty input; let player keep typing
            }

            // Hand the text to the caller. The caller is responsible for invoking
            // Conversation.AddMessage (which will clear IsTyping) — we don't call
            // AddMessage directly here because the caller needs to bridge the
            // 'now' clock from IClock and may want to record additional state.
            var cb = _onSubmit;
            _lockHeld = false; // caller now owns the lock release via AddMessage
            try { cb?.Invoke(text); }
            catch (Exception e) { Debug.LogException(e); }
            Jyx2_UIManager.Instance.HideUI(nameof(AITavernPlayerSpeakPanel));
        }

        void OnCancelClicked()
        {
            if (_conv != null && _lockHeld)
            {
                try { _conv.ClearIsTyping(); } catch { /* swallow — conv may be stopped */ }
                _lockHeld = false;
            }
            var cb = _onCancel;
            try { cb?.Invoke(); }
            catch (Exception e) { Debug.LogException(e); }
            Jyx2_UIManager.Instance.HideUI(nameof(AITavernPlayerSpeakPanel));
        }
    }
}
