using UnityEngine;

namespace Swordman2.Combat
{
    public sealed class SplitCameraFollow : MonoBehaviour
    {
        public Transform Target;
        public Transform Opponent;
        public float Distance = 1.15f;
        public float Height = 2.2f;
        public float ShoulderOffset = 0.7f;
        public float Smoothness = 12f;

        private void LateUpdate()
        {
            if (Target == null || Opponent == null) return;
            Vector3 forward = Opponent.position - Target.position;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.001f) forward = Target.forward;
            else forward.Normalize();

            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            Vector3 desired = Target.position - forward * Distance + right * ShoulderOffset + Vector3.up * Height;
            float t = 1f - Mathf.Exp(-Smoothness * Time.deltaTime);
            transform.position = Vector3.Lerp(transform.position, desired, t);
            Vector3 focus = Opponent.position + Vector3.up * 1.18f;
            Quaternion rotation = Quaternion.LookRotation(focus - transform.position, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, rotation, t);
        }
    }
}
