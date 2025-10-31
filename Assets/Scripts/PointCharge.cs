using UnityEngine;

[System.Serializable]
public class PointCharge
{
   public Vector3 Position;
   [Range(-10,10)]
   public float Charge;
}
