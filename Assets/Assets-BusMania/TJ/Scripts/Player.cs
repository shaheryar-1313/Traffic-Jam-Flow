using System.Collections;
using DG.Tweening;
using Game;
using UnityEngine;

namespace TJ.Scripts
{
    public class Player : MonoBehaviour
    {
        private static readonly int Sit = Animator.StringToHash("Sit");
        public static readonly int Walk = Animator.StringToHash("Walk");
        public ColorEnum color;
        public Renderer meshRenderer;
        public Animator anim;
        public GameObject animGo;

        private void Awake()
        {
            anim = animGo.GetComponent<Animator>();
        }

        public void ChangeColor(ColorEnum colorEnum)
        {
            Material mats = VehicleController.instance.stickmanMaterialHolder.FindMaterialByName(colorEnum);
            meshRenderer.material = mats;
            //gameObject.name = gameObject.name.Replace("blue", "");
            gameObject.name += colorEnum.ToString();
            color = colorEnum;
        }

        public IEnumerator MoveToSlot1(Vector3 mid, Transform pickpoint, Vector3 point, float delay)
        {
            yield return new WaitForSeconds(delay);
            transform.DOMove(mid, 0.3f).OnComplete(() =>
            {
                transform.rotation = pickpoint.rotation;
                transform.DOMove(point, 0.3f).OnComplete(() =>
                {
                    anim.SetBool(Walk, false);
                });
            });
        }
        public IEnumerator MoveToSlot2(Vector3 point, float delay)
        {
            yield return new WaitForSeconds(delay);
            //DOVirtual.DelayedCall(0.2f, () =>
            //{          
            transform.DOMove(point, 0.3f).OnComplete(() =>
            {
                anim.SetBool(Walk, false);
            });
            //});
        }

        /// <summary>
        /// Boards this passenger onto <paramref name="vehicle"/>.
        ///
        /// Because the vehicle is riding the conveyor belt (moving), the world-space
        /// path approach does not work cleanly.  Instead:
        ///   1. Reserve a seat via <see cref="Vehicle.GetFreeSeat"/> (increments seat count).
        ///   2. Tween the passenger's world-space position toward the reserved seat.
        ///   3. Parent this transform to the seat once the passenger reaches it.
        /// </summary>
        public void MoveToTruck(Vehicle vehicle, bool playSound)
        {
            if (!gameObject.activeSelf)
                gameObject.SetActive(true);

            Transform seat = vehicle.GetFreeSeat();
            if (seat == null)
            {
                // Vehicle is full — put passenger back and bail out cleanly.
                return;
            }

            ConveyorFollowerBoard board = vehicle.GetComponentInParent<ConveyorFollowerBoard>();
            board?.BeginPassengerBoarding();

            // Remove from the active queue before parenting to avoid list mutation issues.
            PlayerManager.instance.playersInScene.Remove(this);

            // We don't parent immediately so that the passenger doesn't slide with the moving vehicle.
            // Instead, we interpolate in world space towards the seat's current position,
            // and parent once they reach the seat.
            anim.SetBool(Walk, true);

            Vector3 startPos = transform.position;
            Vector3 seatedLocalPos = new Vector3(0f, -0.34f, 0.2f);
            bool reservationActive = true;

            DOTween.To(() => 0f, x =>
            {
                if (this == null || seat == null) return;

                Vector3 targetPos = seat.TransformPoint(seatedLocalPos);
                transform.position = Vector3.Lerp(startPos, targetPos, x);

                Vector3 lookDirection = targetPos - transform.position;
                lookDirection.y = 0;
                if (lookDirection.sqrMagnitude > 0.001f)
                {
                    transform.rotation = Quaternion.LookRotation(lookDirection);
                }
            }, 1f, 0.7f)
            .SetEase(Ease.InOutSine)
            .SetLink(gameObject)
            .OnKill(() =>
            {
                if (reservationActive)
                    vehicle.ReleaseSeatReservation(seat);
            })
            .OnComplete(() =>
            {
                if (this == null || seat == null) return;

                transform.SetParent(seat);
                transform.localPosition = seatedLocalPos;
                transform.localRotation = Quaternion.identity;
                transform.localScale    = new Vector3(0.8f, 0.8f, 0.8f);

                anim.SetBool(Walk, false);
                anim.SetBool(Sit, true);
                board?.EndPassengerBoarding();
                vehicle.ReleaseSeatReservation(seat);
                reservationActive = false;
            });

            VehicleController.instance.UpdatePlayerCount();
            StartCoroutine(PlayerManager.instance.RepositionPlayers(this));

            if (playSound)
                SoundController.Instance.PlayOneShot(SoundController.Instance.sort);
        }
    }
}
