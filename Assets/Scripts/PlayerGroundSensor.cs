using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(BoxCollider2D))]
public class PlayerGroundSensor : MonoBehaviour
{
    [SerializeField] private LayerMask groundMask;
    public bool IsGrounded => _contacts > 0;
    int _contacts;
    void Awake()
    {
        // Make sure collider is a thin horizontal trigger strip
        var box = GetComponent<BoxCollider2D>();
        box.isTrigger = true;
    }
    void LateUpdate()
    {
        // Keep sensor axis-aligned, even if player rotates
        transform.rotation = Quaternion.identity;
    }
    void OnTriggerEnter2D(Collider2D other)
    {
        if (((1 << other.gameObject.layer) & groundMask) != 0)
        {
            _contacts++;
        }
    }

    void OnTriggerExit2D(Collider2D other)
    {
        if (((1 << other.gameObject.layer) & groundMask) != 0)
        {
            _contacts--;
        }
    }
}