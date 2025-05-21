using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    [SerializeField] private Transform target;
    [SerializeField] private float smoothTime = 0.3f;
    [SerializeField] private Vector3 offset = new Vector3(0, 1, -10);
    [SerializeField] private float lookAheadFactor = 3;
    [SerializeField] private float lookAheadReturnSpeed = 0.5f;
    [SerializeField] private float lookAheadMoveThreshold = 0.1f;

    private float currentLookAheadX = 0;
    private float targetLookAheadX = 0;
    private Vector3 currentVelocity;
    private Vector2 lastTargetPosition;

    void Start()
    {
        if (target == null && GameObject.FindGameObjectWithTag("Player"))
            target = GameObject.FindGameObjectWithTag("Player").transform;

        lastTargetPosition = target ? new Vector2(target.position.x, target.position.y) : Vector2.zero;
    }

    void LateUpdate()
    {
        if (target == null) return;

        // Calculate look-ahead
        Vector2 currentTargetPosition = new Vector2(target.position.x, target.position.y);
        float moveDirection = Mathf.Sign(currentTargetPosition.x - lastTargetPosition.x);

        if (Mathf.Abs(currentTargetPosition.x - lastTargetPosition.x) > lookAheadMoveThreshold)
        {
            targetLookAheadX = lookAheadFactor * moveDirection;
        }
        else
        {
            targetLookAheadX = Mathf.MoveTowards(targetLookAheadX, 0, lookAheadReturnSpeed * Time.deltaTime);
        }

        currentLookAheadX = Mathf.SmoothDamp(currentLookAheadX, targetLookAheadX, ref currentVelocity.x, smoothTime);

        // Calculate target position
        Vector3 targetPosition = new Vector3(
            target.position.x + currentLookAheadX,
            target.position.y + offset.y,
            offset.z
        );

        // Smooth follow
        transform.position = Vector3.SmoothDamp(transform.position, targetPosition, ref currentVelocity, smoothTime);

        lastTargetPosition = currentTargetPosition;
    }
}