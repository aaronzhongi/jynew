// T13: Message-rendering bridge between Conversation.AddMessage and jynew's
// existing ChatUIPanel.
//
// Lives OUTSIDE the `AITavern.Runtime` asmdef (the `Bridge/` folder has no
// .asmdef of its own) so it falls into Assembly-CSharp and can reference
// ChatUIPanel / Jyx2_UIManager / StoryEngine from the game core.
//
// Behavior (per Plan §2.7 + §4.7, INV-2.7):
//   - Per-message modal flag: NPC↔NPC turns do NOT set
//     StoryEngine.BlockPlayerControl. Player turns do.
//   - Auto-dismiss (NPC_BUBBLE_AUTO_DISMISS_SEC = 6 s) on non-player turns so
//     the FSM doesn't stall on a player who isn't clicking.
//   - Global queue: ChatUIPanel.IsOnly = true so only one panel can be open at
//     a time; AITavernBubbleUI owns the queue.
//   - Cooldown anchoring: MESSAGE_COOLDOWN_MS in the FSM is anchored to
//     Conversation.LastMessage.Timestamp (message creation), NOT bubble
//     dismissal. Bubbles and FSM advance on independent clocks.
//
// NOT wired up to anything yet — T15 (AITavernBoot/AgentSimulator) will
// subscribe Conversation.AddMessage events or call EnqueueMessage directly.
using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Jyx2;

namespace Jyx2.AITavern.Bridge
{
    /// <summary>
    /// Routes Conversation messages to jynew's ChatUIPanel with per-message
    /// modal flag, auto-dismiss timer, and a global single-bubble queue.
    /// </summary>
    [DefaultExecutionOrder(-150)]
    public class AITavernBubbleUI : MonoBehaviour
    {
        const float NPC_BUBBLE_AUTO_DISMISS_SEC = 6.0f;

        public static AITavernBubbleUI Instance { get; private set; }

        struct QueuedMessage
        {
            public int HeadId;
            public string Text;
            public bool IsPlayerTurn;     // when true: set StoryEngine.BlockPlayerControl during show
            public Action OnDismissed;    // optional callback after the bubble dismisses (manual click or auto)
        }

        readonly Queue<QueuedMessage> _queue = new Queue<QueuedMessage>();
        bool _isShowing;

        // True while a bubble is on screen OR there are queued messages
        // waiting to render. AgentSimulator gates the player speak panel on
        // this so the NPC's reply actually finishes displaying before the
        // panel steals the ChatUIPanel slot (Jyx2_UIManager treats both as
        // IsOnly=true and only allows one panel up at a time).
        public bool IsBusy => _isShowing || _queue.Count > 0;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>
        /// Enqueue a message. Returns immediately. The bubble may render now
        /// (if idle) or after earlier queued messages dismiss.
        /// </summary>
        public void EnqueueMessage(int headId, string text, bool isPlayerTurn, Action onDismissed = null)
        {
            _queue.Enqueue(new QueuedMessage
            {
                HeadId = headId,
                Text = text,
                IsPlayerTurn = isPlayerTurn,
                OnDismissed = onDismissed,
            });
            if (!_isShowing) PumpQueueAsync().Forget();
        }

        async UniTaskVoid PumpQueueAsync()
        {
            if (_isShowing) return;
            _isShowing = true;
            try
            {
                while (_queue.Count > 0)
                {
                    var msg = _queue.Dequeue();
                    await ShowOneAsync(msg);
                }
            }
            finally
            {
                _isShowing = false;
            }
        }

        async UniTask ShowOneAsync(QueuedMessage msg)
        {
            bool dismissed = false;
            void Dismiss() { dismissed = true; }

            if (msg.IsPlayerTurn)
            {
                StoryEngine.BlockPlayerControl = true;
            }

            // ChatUIPanel.OnShowPanel arg shape (verified at
            // jyx2/Assets/Scripts/UI/ChatUIPanel.cs:62-77):
            //   allParams[0] = ChatType         (cast from int)
            //   allParams[1] = headId           (int)
            //   allParams[2] = text             (string)
            //   allParams[3] = type             (int; 1 = portrait+text path)
            //   allParams[4] = callback         (Action; fires on click-to-advance dismiss)
            try
            {
                await Jyx2_UIManager.Instance.ShowUIAsync(
                    nameof(ChatUIPanel),
                    ChatType.RoleId,
                    msg.HeadId,
                    msg.Text,
                    1,                              // type 1 = portrait path
                    (Action)(() => Dismiss()));

                // For NPC↔NPC turns, pop the ChatUIInputContext from the input
                // stack so the player keeps movement control while the bubble
                // is visible. The panel stays on screen; only its input grab
                // is released. Player-turn messages keep the context (we WANT
                // the player focused on responding then).
                if (!msg.IsPlayerTurn)
                {
                    var chatPanel = Jyx2_UIManager.Instance.GetUI<ChatUIPanel>();
                    if (chatPanel != null)
                    {
                        var ctx = chatPanel.GetComponent<Jyx2.InputCore.UI.ChatUIInputContext>();
                        if (ctx != null)
                        {
                            Jyx2.InputCore.InputContextManager.Instance.RemoveInputContext(ctx);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[AITavernBubbleUI] ShowUIAsync failed: {e}");
                dismissed = true;
            }

            // Wait for dismissal: manual click OR (non-player turn) auto-dismiss
            // after N seconds. Use unscaledDeltaTime so a paused/slowed timeScale
            // doesn't stretch the bubble.
            float elapsed = 0f;
            while (!dismissed)
            {
                await UniTask.Yield();
                elapsed += Time.unscaledDeltaTime;
                if (!msg.IsPlayerTurn && elapsed >= NPC_BUBBLE_AUTO_DISMISS_SEC)
                {
                    // Force-hide the panel since the player didn't click.
                    try { Jyx2_UIManager.Instance.HideUI(nameof(ChatUIPanel)); } catch { }
                    dismissed = true;
                }
            }

            if (msg.IsPlayerTurn)
            {
                StoryEngine.BlockPlayerControl = false;
            }

            try { msg.OnDismissed?.Invoke(); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }
}
