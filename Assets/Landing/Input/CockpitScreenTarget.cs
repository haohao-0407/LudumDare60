using UnityEngine;

namespace LudumDare60.Landing
{
    [DisallowMultipleComponent]
    public sealed class CockpitScreenTarget : MonoBehaviour
    {
        [Tooltip("此显示屏的固定机位；相机朝向使用此节点的蓝色 Z 轴。")]
        public Transform ViewPoint;
        [Range(15, 90)] public float FieldOfView = 45;
        [Tooltip("显示屏的专用碰撞体，只点击显示面才进入固定视角。")]
        public Collider HitCollider;

        private void OnDrawGizmosSelected()
        {
            if (ViewPoint == null) return;
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(ViewPoint.position, transform.position);
            var old = Gizmos.matrix;
            Gizmos.matrix = ViewPoint.localToWorldMatrix;
            Gizmos.DrawFrustum(Vector3.zero, FieldOfView, 0.35f, 0.025f, 16f / 9f);
            Gizmos.matrix = old;
        }
    }
}
