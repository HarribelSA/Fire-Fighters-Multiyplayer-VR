using UnityEngine;

public class WheelVisuals : MonoBehaviour
{
    [SerializeField] Transform[] wheelMeshes;
    WheelCollider[] wheels;

    void Start()
    {
        wheels = GetComponentsInChildren<WheelCollider>();
    }

    void Update()
    {
        RotateWheelMesh();
    }

    void RotateWheelMesh()
    {
        for (int i = 0; i < wheels.Length; i++)
        {
            UpdateWheel(wheels[i], wheelMeshes[i]);
        }
    }

    void UpdateWheel(WheelCollider collider, Transform mesh)
    {
        Vector3 pos;
        Quaternion rot;
        collider.GetWorldPose(out pos, out rot);
        mesh.position = pos;
        mesh.rotation = rot;
    }
}
