using UnityEngine;
using System.Collections.Generic;

public class CognitionBoard : MonoBehaviour
{
    public GameObject nodePrefab;
    public Transform nodesParent;
    public List<ClueNode> nodes = new List<ClueNode>();
    public List<ConnectionLine> connections = new List<ConnectionLine>();

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
                    float repulsionForce = 10f / (distance * distance); // repel each other
                    force += direction.normalized * repulsionForce;
                }
            }

            // Attractive force along connections
            foreach (var conn in connections)
            {
                if (conn.IsConnectedTo(node))
                {
                    var otherNode = conn.GetOtherNode(node);
                    Vector3 direction = otherNode.transform.localPosition - node.transform.localPosition;
                    float distance = direction.magnitude;
                    float springForce = (distance - 100f) * 0.5f * conn.strength; // spring-like force
                    force += direction.normalized * springForce;
                }
            }

            // Limit maximum force
            float maxForce = 10f;
            force = Vector3.ClampMagnitude(force, maxForce);

            // Update position based on force
            node.SetTargetPosition(node.transform.localPosition + force * Time.deltaTime);
        }
    }

    // Add nodes
    public void AddNode(string name, int id)
    {
        GameObject nodeObj = Instantiate(nodePrefab, nodesParent);
        ClueNode node = nodeObj.GetComponent<ClueNode>();
        node.nodeName = name;
        node.nodeID = id;
        nodes.Add(node);
    }

    // Connect nodes
    public void ConnectNodes(ClueNode a, ClueNode b, float strength)
    {
        // Instantiate a line object
        ConnectionLine line = InstantiateLine(a.transform.position, b.transform.position);
        line.Initialize(a, b, strength);
        connections.Add(line);
        // Update node positions after connection
        UpdateNodePositions();
    }

    private ConnectionLine InstantiateLine(Vector3 start, Vector3 end)
    {
        GameObject lineObj = new GameObject("ConnectionLine");
        lineObj.transform.parent = this.transform; // parent to the CognitionBoard
        LineRenderer lr = lineObj.AddComponent<LineRenderer>();
        // Assign a material as needed
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
            position = node.transform.localPosition; // no connection, stay put

        return position;
    }
}
