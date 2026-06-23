using UnityEngine;
using TJ.Scripts;

public class BarrelScript : MonoBehaviour
{
    private Animator _animator;
    private int _vehiclesInTrigger = 0;

    void Start()
    {
        _animator = GetComponent<Animator>();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.GetComponentInParent<Vehicle>() != null)
        {
            _vehiclesInTrigger++;
            if (_vehiclesInTrigger == 1 && _animator != null)
            {
                _animator.SetBool("isOpen", true);
            }
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.GetComponentInParent<Vehicle>() != null)
        {
            _vehiclesInTrigger--;
            if (_vehiclesInTrigger <= 0)
            {
                _vehiclesInTrigger = 0;
                if (_animator != null)
                {
                    _animator.SetBool("isOpen", false);
                }
            }
        }
    }
}
