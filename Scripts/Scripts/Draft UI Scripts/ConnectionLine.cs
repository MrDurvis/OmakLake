using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class ConnectionLine : MonoBehaviour
{
    public LineRenderer lineRenderer;

    // References to the connected nodes
    public ClueNode nodeA;
    public ClueNode nodeB;

    // Connection strength (0 to 1)
    public float strength = 0f;

    // Initialize the line with nodes and strength
    public void Initialize(ClueNode a, ClueNode b, float connectionStrength)
    {
        nodeA = a;
        nodeB = b;
        SetStrength(connectionStrength);
        UpdateLinePositions();
    }

    void Awake()
    {
        lineRenderer = GetComponent<LineRenderer>();
        // Optional: set LineRenderer properties here
        lineRenderer.positionCount = 2;
        lineRenderer.useWorldSpace = false; // for local positioning
    }

    // Update is called once per frame
    void Update()
    {
        if (nodeA != null && nodeB != null)
        {
            UpdateLinePositions();
        }
    }

    // Set the connection strength (0 to 1)
    public void SetStrength(float s)
    {
        strength = Mathf.Clamp01(s);
        UpdateVisuals();
    }

    // Update visual appearance based on strength
    private void UpdateVisuals()
    {
        Color color = Color.green * strength;
        lineRenderer.startColor = color;
        lineRenderer.endColor = color;
        float width = 0.02f + 0.03f * strength; // Width varies with strength
        lineRenderer.startWidth = width;
        lineRenderer.endWidth = width;
    }

    // Update the positions of the line endpoints
    private void UpdateLinePositions()
    {
        lineRenderer.SetPosition(0, nodeA.transform.localPosition);
        lineRenderer.SetPosition(1, nodeB.transform.localPosition);
    }

    // Optional: if you want to animate the line when nodes move, you could do so here
    
    public bool IsConnectedTo(ClueNode node)
    {
        return nodeA == node || nodeB == node;
    }

    public ClueNode GetOtherNode(ClueNode node)
    {
        if (nodeA == node)
            return nodeB;
        else if (nodeB == node)
            return nodeA;
                else
                    return null; // Or throw an exception if needed
            }
        }