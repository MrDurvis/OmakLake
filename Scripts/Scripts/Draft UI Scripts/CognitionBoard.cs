using UnityEngine;
using System.Collections.Generic;

public class CognitionBoard : MonoBehaviour
{
    public GameObject nodePrefab; // your ClueNode prefab
    public Transform nodesParent; // parent transform for nodes
    public List<ClueNode> nodes = new List<ClueNode>(); // track nodes
    public List<ConnectionLine> connections = new List<ConnectionLine>(); // connection lines

    void Update()
    {
        foreach (var node in nodes)
        {
            Vector3 force = Vector3.zero;

            // Repulsion from other nodes
            foreach (var other in nodes)
            {
                if (other != node)
                {
                    Vector3 direction = node.transform.localPosition - other.transform.localPosition;
                    float distance = direction.magnitude + 0.1f; // avoid division by zero
                    float repulsionForce = 10f / (distance * distance); // repel
                    force += direction.normalized * repulsionForce;
                }
            }

            // Attractive Force along connections
            foreach (var conn in connections)
            {
                if (conn.IsConnectedTo(node))
                {
                    var otherNode = conn.GetOtherNode(node);
                    Vector3 direction = otherNode.transform.localPosition - node.transform.localPosition;
                    float distance = direction.magnitude;
                    float springForce = (distance - 100f) * 0.5f * conn.strength; // spring formula
                    force += direction.normalized * springForce;
                }
            }

            // Limit force magnitude
            float maxForce = 10f;
            force = Vector3.ClampMagnitude(force, maxForce);

            // Apply force as target position
            node.SetTargetPosition(node.transform.localPosition + force * Time.deltaTime);
        }
    }

    // Add a new node with ClueData
    public void AddNode(ClueData clueData)
    {
        GameObject nodeObj = Instantiate(nodePrefab, nodesParent);
        ClueNode node = nodeObj.GetComponent<ClueNode>();
        node.Initialize(clueData); // pass ClueData object
        nodes.Add(node);
    }

    // Connect two nodes, use their nodeID or reference
    public void ConnectNodes(ClueNode a, ClueNode b, float strength)
    {
        ConnectionLine line = InstantiateLine(a.transform.localPosition, b.transform.localPosition);
        line.Initialize(a, b, strength);
        connections.Add(line);
        UpdateNodePositions();
    }

    private ConnectionLine InstantiateLine(Vector3 start, Vector3 end)
    {
        GameObject lineObj = new GameObject("ConnectionLine");
        lineObj.transform.parent = this.transform; // parent to the board
        LineRenderer lr = lineObj.AddComponent<LineRenderer>();
        lr.material = new Material(Shader.Find("Sprites/Default"));
        lr.positionCount = 2;
        ConnectionLine line = lineObj.AddComponent<ConnectionLine>();
        return line;
    }

    void UpdateNodePositions()
    {
        foreach (var node in nodes)
        {
            Vector3 newPos = CalculatePosition(node);
            node.SetTargetPosition(newPos);
        }
    }

    Vector3 CalculatePosition(ClueNode node)
    {
        Vector3 position = Vector3.zero;
        float totalWeight = 0f;

        foreach (var conn in connections)
        {
            if (conn.IsConnectedTo(node))
            {
                var otherNode = conn.GetOtherNode(node);
                float weight = conn.strength;
                position += otherNode.transform.localPosition * weight;
                totalWeight += weight;
            }
        }

        if (totalWeight > 0)
            position /= totalWeight;
        else
            position = node.transform.localPosition;

        return position;
    }
}
