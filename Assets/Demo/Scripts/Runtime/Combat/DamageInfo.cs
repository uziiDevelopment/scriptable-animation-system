using Unity.Netcode;
using UnityEngine;

namespace Demo.Scripts.Runtime.Combat
{
    /// <summary>
    /// Network-serializable damage information
    /// </summary>
    public struct DamageInfo : INetworkSerializable
    {
        public float Damage;
        public Vector3 HitPoint;
        public Vector3 HitNormal;
        public ulong AttackerClientId;
        public bool IsHeadshot;

        public DamageInfo(float damage, Vector3 hitPoint, Vector3 hitNormal, ulong attackerClientId, bool isHeadshot = false)
        {
            Damage = damage;
            HitPoint = hitPoint;
            HitNormal = hitNormal;
            AttackerClientId = attackerClientId;
            IsHeadshot = isHeadshot;
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Damage);
            serializer.SerializeValue(ref HitPoint);
            serializer.SerializeValue(ref HitNormal);
            serializer.SerializeValue(ref AttackerClientId);
            serializer.SerializeValue(ref IsHeadshot);
        }
    }
}
