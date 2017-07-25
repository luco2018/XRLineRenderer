﻿using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// An XR-Focused drop-in replacement for the Line Renderer
/// This renderer draws fixed-width lines with simulated volume and glow.
/// This has many of the advantages of the traditional Line Renderer, old-school system-level line rendering functions, 
/// and volumetric (a linked series of capsules or cubes) rendering
/// </summary>
[RequireComponent(typeof(MeshRenderer))]
[RequireComponent(typeof(MeshFilter))]
[ExecuteInEditMode]
public class XRLineRenderer : XRLineRendererBase
{
      // Stored Line Data
    [SerializeField]
    [Tooltip("All of the connected points to render as a line.")]
    Vector3[] m_Positions;

    [SerializeField]
    [FormerlySerializedAs("m_WorldSpaceData")]
    [Tooltip("Draw lines in worldspace (or local space) - driven via shader.")]
    bool m_UseWorldSpace;

    [SerializeField]
    [Tooltip("Connect the first and last vertices, to create a loop.")]
    bool m_Loop;
    
    /// <summary>
    /// Draw lines in worldspace (or local space)
    /// </summary>
    public bool useWorldSpace
    {
        get
        {
            return m_UseWorldSpace;
        }
        set
        {
            m_UseWorldSpace = value;
        }
    }

    /// <summary>
    /// Makes sure that the internal world space flag of the line renderer
    /// matches the world space flag of the first material on the object
    /// </summary>
    void CopyWorldSpaceDataFromMaterial()
    {
        if (m_Materials != null && m_Materials.Length > 0)
        {
            var firstMaterial = m_Materials[0];
            if (firstMaterial == null)
            {
                return;
            }
            if (firstMaterial.HasProperty("_WorldData"))
            {
                m_UseWorldSpace = !Mathf.Approximately(firstMaterial.GetFloat("_WorldData"), 0.0f);
            }
            else
            {
                m_UseWorldSpace = false;
            }
        }
    }

    /// <summary>
    /// Sets the position of the vertex in the line.
    /// </summary>
    /// <param name="index">Which vertex to set</param>
    /// <param name="position">The new location in space of this vertex</param>
    public void SetPosition(int index, Vector3 position)
    {
        // Update internal data
        m_Positions[index] = position;

        // See if the data needs initializing
        if (Initialize())
        {
            return;
        }

        // Otherwise, do fast setting
        var prevIndex = (index - 1 + m_Positions.Length) % m_Positions.Length;
        var endIndex = (index + 1) % m_Positions.Length;

        if (index > 0 || m_Loop)
        {
            m_XRMeshData.SetElementPipe((index * 2) - 1, ref m_Positions[prevIndex], ref m_Positions[index]);
        }

        m_XRMeshData.SetElementPosition(index * 2, ref m_Positions[index]);
        if (index < (m_Positions.Length - 1) || m_Loop)
        {
            m_XRMeshData.SetElementPipe((index * 2) + 1, ref m_Positions[index], ref m_Positions[endIndex]);
        }
        m_XRMeshData.SetMeshDataDirty(XRMeshChain.MeshRefreshFlag.Positions);
        m_MeshNeedsRefreshing = true;
    }

    /// <summary>
    /// Sets all positions in the line. Cheaper than calling SetPosition repeatedly
    /// </summary>
    /// <param name="newPositions">All of the new endpoints of the line</param>
    /// <param name="knownSizeChange">Turn on to run a safety check to make sure the number of endpoints does not change (bad for garbage collection)</param>
    void SetPositions(Vector3[] newPositions, bool knownSizeChange = false)
    {
        // Update internal data
        if (m_Positions == null || newPositions.Length != m_Positions.Length)
        {
            if (knownSizeChange == false)
            {
                Debug.LogWarning("New positions does not match size of existing array.  Adjusting vertex count as well");
            }
            m_Positions = newPositions;
            Initialize(true);
            return;
        }
        m_Positions = newPositions;

        // See if the data needs initializing
        if (Initialize())
        {
            return;
        }

        // Otherwise, do fast setting
        var pointCounter = 0;
        var elementCounter = 0;
        m_XRMeshData.SetElementPosition(elementCounter, ref m_Positions[pointCounter]);
        elementCounter++;
        pointCounter++;
        while (pointCounter < m_Positions.Length)
        {
            m_XRMeshData.SetElementPipe(elementCounter, ref m_Positions[pointCounter - 1], ref m_Positions[pointCounter]);
            elementCounter++;
            m_XRMeshData.SetElementPosition(elementCounter, ref m_Positions[pointCounter]);

            elementCounter++;
            pointCounter++;
        }
        if (m_Loop)
        {
            m_XRMeshData.SetElementPipe(elementCounter, ref m_Positions[pointCounter - 1], ref m_Positions[0]);
        }

        // Dirty all the VRMeshChain flags so everything gets refreshed
        m_XRMeshData.SetMeshDataDirty(XRMeshChain.MeshRefreshFlag.Positions);
        m_MeshNeedsRefreshing = true;
    }

    /// <summary>
    /// Sets the number of billboard-line chains. This function regenerates the point list if the
    /// number of vertex points changes, so use it sparingly.
    /// </summary>
    /// <param name="count">The new number of vertices in the line</param>
    public void SetVertexCount(int count)
    {
        // See if anything needs updating
        if (m_Positions.Length == count)
        {
            return;
        }

        // Adjust this array
        var newPositions = new Vector3[count];
        var copyCount = Mathf.Min(m_Positions.Length, count);
        var copyIndex = 0;

        while (copyIndex < copyCount)
        {
            newPositions[copyIndex] = m_Positions[copyIndex];
            copyIndex++;
        }
        m_Positions = newPositions;

        // Do an initialization, this changes everything
        Initialize(true);
    }
    
    /// <summary>
    /// Get the number of billboard-line chains.
    /// <summary
    public int GetVertexCount()
    {
        return m_Positions.Length;
    }

    protected override void UpdateColors()
    {
        // See if the data needs initializing
        if (Initialize())
        {
            return;
        }

        // If it doesn't, go through each point and set the data
        var pointCounter = 0;
        var elementCounter = 0;
        var stepPercent = 0.0f;

        var lastColor = m_Color.Evaluate(stepPercent);
        m_XRMeshData.SetElementColor(elementCounter, ref lastColor);
        elementCounter++;
        pointCounter++;
        stepPercent += m_StepSize;

        while (pointCounter < m_Positions.Length)
        {
            var currentColor = m_Color.Evaluate(stepPercent);
            m_XRMeshData.SetElementColor(elementCounter, ref lastColor, ref currentColor);
            elementCounter++;

            m_XRMeshData.SetElementColor(elementCounter, ref currentColor);

            lastColor = currentColor;
            elementCounter++;
            pointCounter++;
            stepPercent += m_StepSize;
        }

        if (m_Loop)
        {
            lastColor = m_Color.Evaluate(stepPercent);
            m_XRMeshData.SetElementColor(elementCounter, ref lastColor);
        }

        // Dirty the color meshChain flags so the mesh gets new data
        m_XRMeshData.SetMeshDataDirty(XRMeshChain.MeshRefreshFlag.Colors);
        m_MeshNeedsRefreshing = true;
    }

    protected override void UpdateWidth()
    {
        // See if the data needs initializing
        if (Initialize())
        {
            return;
        }

        // Otherwise, do fast setting
        var pointCounter = 0;
        var elementCounter = 0;
        var stepPercent = 0.0f;
        
        // We go through the element list, much like initialization, but only update the width part of the variables
        var lastWidth = m_WidthCurve.Evaluate(stepPercent) * m_Width;
        m_XRMeshData.SetElementSize(elementCounter, lastWidth);
        elementCounter++;
        pointCounter++;

        stepPercent += m_StepSize;

        while (pointCounter < m_Positions.Length)
        {
            var currentWidth = m_WidthCurve.Evaluate(stepPercent) * m_Width;

            m_XRMeshData.SetElementSize(elementCounter, lastWidth, currentWidth);
            elementCounter++;
            m_XRMeshData.SetElementSize(elementCounter, currentWidth);
            lastWidth = currentWidth;
            elementCounter++;
            pointCounter++;
            stepPercent += m_StepSize;
        }

        if (m_Loop)
        {
            var currentWidth = m_WidthCurve.Evaluate(stepPercent) * m_Width;
            m_XRMeshData.SetElementSize(elementCounter, lastWidth, currentWidth);
        }

        // Dirty all the VRMeshChain flags so everything gets refreshed
        m_XRMeshData.SetMeshDataDirty(XRMeshChain.MeshRefreshFlag.Sizes);
        m_MeshNeedsRefreshing = true;
    }

    protected override bool Initialize(bool force = false)
    {
        var performFullInitialize = base.Initialize(force);

        CopyWorldSpaceDataFromMaterial();
        if (m_Positions == null)
        {
            return false;
        }
 
        // For a line renderer we assume one big chain
        // We need a control point for each billboard and a control point for each pipe connecting them together
        // Except for the end, which must be capped with another billboard.  This gives us (positions * 2) - 1
        var neededPoints = Mathf.Max((m_Positions.Length * 2) - 1, 0);

        // If we're looping, then we do need one more pipe
        if (m_Loop)
        {
            neededPoints++;
        }

        if (m_XRMeshData == null)
        {
            m_XRMeshData = new XRMeshChain();
        }
        if (m_XRMeshData.reservedElements != neededPoints)
        {
            m_XRMeshData.worldSpaceData = useWorldSpace;
            m_XRMeshData.GenerateMesh(gameObject, true, neededPoints);
            performFullInitialize = true;
        }
        if (performFullInitialize == false)
        {
            return false;
        }
        if (neededPoints == 0)
        {
            m_StepSize = 1.0f;
            return true;
        }
        m_StepSize = 1.0f / Mathf.Max(m_Loop ? m_Positions.Length : m_Positions.Length - 1, 1.0f);

        var pointCounter = 0;
        var elementCounter = 0;
        var stepPercent = 0.0f;

        var lastColor = m_Color.Evaluate(stepPercent);
        var lastWidth = m_WidthCurve.Evaluate(stepPercent) * m_Width;

        // Initialize the single starting point
        m_XRMeshData.SetElementSize(elementCounter, lastWidth);
        m_XRMeshData.SetElementPosition(elementCounter, ref m_Positions[pointCounter]);
        m_XRMeshData.SetElementColor(elementCounter, ref lastColor);
        elementCounter++;
        pointCounter++;

        stepPercent += m_StepSize;
        

        // Now do the chain
        while (pointCounter < m_Positions.Length)
        {
            var currentWidth = m_WidthCurve.Evaluate(stepPercent) * m_Width;
            var currentColor = m_Color.Evaluate(stepPercent);

            // Create a pipe from the previous point to here
            m_XRMeshData.SetElementSize(elementCounter, lastWidth, currentWidth);
            m_XRMeshData.SetElementPipe(elementCounter, ref m_Positions[pointCounter - 1], ref m_Positions[pointCounter]);
            m_XRMeshData.SetElementColor(elementCounter, ref lastColor, ref currentColor);
            elementCounter++;

            // Now record our own point data
            m_XRMeshData.SetElementSize(elementCounter, currentWidth);
            m_XRMeshData.SetElementPosition(elementCounter, ref m_Positions[pointCounter]);
            m_XRMeshData.SetElementColor(elementCounter, ref currentColor);

            // Go onto the next point while retaining previous values we might need to lerp between
            lastWidth = currentWidth;
            lastColor = currentColor;
            elementCounter++;
            pointCounter++;
            stepPercent += m_StepSize;
        }

        if (m_Loop)
        {
            var currentWidth = m_WidthCurve.Evaluate(stepPercent) * m_Width;
            var currentColor = m_Color.Evaluate(stepPercent);
            m_XRMeshData.SetElementSize(elementCounter, lastWidth, currentWidth);
            m_XRMeshData.SetElementPipe(elementCounter, ref m_Positions[pointCounter - 1], ref m_Positions[0]);
            m_XRMeshData.SetElementColor(elementCounter, ref lastColor, ref currentColor);
        }

        // Dirty all the VRMeshChain flags so everything gets refreshed
        m_XRMeshData.SetMeshDataDirty(XRMeshChain.MeshRefreshFlag.All);
        m_MeshNeedsRefreshing = true;
        return true;
    }

    protected override bool NeedsReinitialize()
    {
        // No mesh data means we definately need to reinitialize
        if (m_XRMeshData == null)
        {
            return true;
        }

        // If we have any positions, figure out how many points we need to render a line along it
        var neededPoints = 0;
        if (m_Positions != null)
        {
            neededPoints = Mathf.Max((m_Positions.Length * 2) - 1, 0);
            if (m_Loop)
            {
                neededPoints++;
            }
        }

        return (m_XRMeshData.reservedElements != neededPoints);
    }
}
