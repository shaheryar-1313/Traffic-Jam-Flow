using System.Collections;
using DG.Tweening;
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
        ///   2. Parent this transform to the seat immediately so the passenger rides with
        ///      the moving vehicle from this point on.
        ///   3. Tween the LOCAL position from the initial offset (passenger appears at their
        ///      grid position in vehicle-local space) to the seated local offset.
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

            // Remove from the active queue before parenting to avoid list mutation issues.
            PlayerManager.instance.playersInScene.Remove(this);

            // Parent to the seat — Unity keeps world position by default, so the passenger
            // appears at its current grid world position expressed in the seat's local space.
            transform.SetParent(seat);

            anim.SetBool(Walk, true);

            // Tween local position to the seated offset.  Since we are now in the seat's
            // local space, this animates the passenger "running" to their seat while the
            // vehicle moves beneath them.
            Vector3 seatedLocalPos = new Vector3(0f, -0.34f, 0.2f);
            transform.DOLocalMove(seatedLocalPos, 0.7f)
                .SetEase(Ease.InOutSine)
                .OnComplete(() =>
                {
                    anim.SetBool(Walk, false);
                    anim.SetBool(Sit, true);
                    transform.localRotation = Quaternion.identity;
                    transform.localScale    = new Vector3(0.8f, 0.8f, 0.8f);
                });

            VehicleController.instance.UpdatePlayerCount();
            StartCoroutine(PlayerManager.instance.RepositionPlayers(this));

            if (playSound)
                SoundController.Instance.PlayOneShot(SoundController.Instance.sort);
        }
    }
}
