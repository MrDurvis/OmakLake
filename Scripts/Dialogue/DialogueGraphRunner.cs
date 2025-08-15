using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DialogueGraphRunner : MonoBehaviour
{
    private static DialogueGraphRunner _runner;
    private static DialogueGraphRunner Runner
    {
        get
        {
            if (_runner == null)
            {
                var go = new GameObject("DialogueGraphRunner");
                _runner = go.AddComponent<DialogueGraphRunner>();
                DontDestroyOnLoad(go);
            }
            return _runner;
        }
    }

    /// <summary>
    /// Plays a DialogueGraph via the existing DialogueManager UI.
    /// Returns pickupApproved=true if the player selects a choice with semantic PickupYes anywhere in the flow.
    /// </summary>
    public static void Play(DialogueGraph graph, Action<GraphRunResult> onComplete)
    {
        if (graph == null)
        {
            Debug.LogWarning("[DialogueGraphRunner] Graph is null.");
            onComplete?.Invoke(new GraphRunResult { pickupApproved = false });
            return;
        }
        Runner.StartCoroutine(Runner.RunGraph(graph, onComplete));
    }

    private IEnumerator RunGraph(DialogueGraph graph, Action<GraphRunResult> onComplete)
    {
        var dm = DialogueManager.Instance;
        if (dm == null)
        {
            Debug.LogError("[DialogueGraphRunner] DialogueManager.Instance is null.");
            onComplete?.Invoke(new GraphRunResult { pickupApproved = false });
            yield break;
        }

        string cur = graph.startGuid;
        bool pickupApproved = false;

        while (!string.IsNullOrEmpty(cur))
        {
            DialogueNode node = graph.Get(cur);
            if (node == null)
            {
                Debug.LogWarning($"[GraphRunner] Node '{cur}' not found. Ending.");
                break;
            }

            if (!node.isChoice)
            {
                // TEXT node
                bool done = false;
                dm.StartSequence(
                    new List<DialogueBlock> {
                        new DialogueBlock { type = BlockType.Text, text = node.text }
                    },
                    _ => { done = true; }
                );
                while (!done) yield return null;

                cur = node.nextGuid; // advance to next (may be null/empty to end)
            }
            else
            {
                // CHOICE node
                string prompt = string.IsNullOrEmpty(node.text) ? "" : node.text;

                var options = new List<string>();
                foreach (var ch in node.choices) options.Add(ch?.label ?? "Option");

                int def = 0;
                if (options.Count > 0)
                    def = Mathf.Clamp(node.defaultChoiceIndex, 0, options.Count - 1);

                bool done = false;
                SequenceResult result = null;

                dm.StartSequence(
                    new List<DialogueBlock> {
                        new DialogueBlock {
                            type = BlockType.Choice,
                            prompt = prompt,
                            options = options,
                            defaultIndex = def
                        }
                    },
                    r => { result = r; done = true; }
                );
                while (!done) yield return null;

                int pickedIndex = Mathf.Clamp(result?.lastChoiceIndex ?? def, 0, Math.Max(0, options.Count - 1));
                var picked = (pickedIndex >= 0 && pickedIndex < node.choices.Count) ? node.choices[pickedIndex] : null;

                // Map semantics explicitly
                if (picked != null)
                {
                    switch (picked.semantic)
                    {
                        case ChoiceSemantic.PickupYes:
                            pickupApproved = true;
                            break;
                        case ChoiceSemantic.PickupNo:
                            pickupApproved = false;
                            break;
                        case ChoiceSemantic.None:
                        default:
                            // no change
                            break;

                        // If you still use a legacy PickupYesNo somewhere, uncomment:
                        // case ChoiceSemantic.PickupYesNo:
                        //     pickupApproved = true; // treat as YES by convention
                        //     break;
                    }
                }

                Debug.Log($"[GraphRunner] Picked index={pickedIndex} label='{picked?.label}' semantic={picked?.semantic} => pickupApproved={pickupApproved}");

                // follow the chosen branch
                cur = picked?.nextGuid;
            }
        }

        onComplete?.Invoke(new GraphRunResult { pickupApproved = pickupApproved });
    }
}

public struct GraphRunResult
{
    public bool pickupApproved;
}
