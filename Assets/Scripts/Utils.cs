using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>
/// This class provides extention methods to common .NET data types.
/// </summary>
public static class Utils
{
    /// <summary>
    /// Small threshold to be consider practically zero.
    /// </summary>
    public const double EPSILON = 1e-6;

    /// <summary>
    /// Conversion factor from radians to degrees.
    /// </summary>
    public const float RAD2DEG = 180.0f / Mathf.PI;

    /// <summary>
    /// Conversion factor from degrees to radians.
    /// </summary>
    public const float DEG2RAD = Mathf.PI / 180.0f;

    /// <summary>
    /// The constant <see cref="Math.PI"/> divided by two.
    /// </summary>
    public const float PI_HALF = Mathf.PI / 2.0f;

    /// <summary>
    /// Converts the value of an angle from radians to degrees.
    /// </summary>
    /// <param name="angleRad">Angle in radians</param>
    /// <returns>The angle in degrees</returns>
    public static float ToDegrees(this float angleRad)
    {
        return angleRad * RAD2DEG;
    }

    /// <summary>
    /// Converts the value of an angle from degrees to radians.
    /// </summary>
    /// <param name="angleDeg">Angle in degrees</param>
    /// <returns>The angle in radians</returns>
    public static float ToRadians(this float angleDeg)
    {
        return angleDeg * DEG2RAD;
    }

    /// <summary>
    /// Normalizes an angle in degrees between the range of [reference, reference + 360).
    /// </summary>
    /// <param name="angleDeg">angle in degrees</param>
    /// <param name="referenceDeg">Reference in degrees</param>
    /// <returns>The normalized angle in degrees</returns>
    public static float NormalizeDegrees(this float angleDeg, float referenceDeg = -180.0f)
    {
        float result = (float)Math.IEEERemainder(angleDeg - referenceDeg, 360.0);
        return result < 0.0f ? 360.0f + referenceDeg + result : referenceDeg + result;
    }

    /// <summary>
    /// Normalizes an angle in radians between the range of [reference, reference + 2 PI).
    /// </summary>
    /// <param name="angleRad">Angle in radians</param>
    /// <param name="referenceRad">Reference in radians</param>
    /// <returns>The normalized angle in radians</returns>
    public static float NormalizeRadians(this float angleRad, float referenceRad = (float)-Math.PI)
    {
        float result = (float)Math.IEEERemainder(angleRad - referenceRad, 2.0 * Math.PI);
        return result < 0.0f ? 2.0f * (float)Math.PI + referenceRad + result : referenceRad + result;
    }

    /// <summary>
    /// Returns the nominal solution of Euler angles for the specified instance. The nominal Euler angle set is 
    /// the solution with angles closer to 0.
    /// </summary>
    /// <param name="angles1">A Euler angles rotation in radians.</param>
    /// <returns>The nominal Euler angles solution in radians.</returns>
    public static Vector3 NominalEulerAngles(this Vector3 angles1)
    {
        angles1 = new Vector3(angles1.x.NormalizeDegrees(), angles1.y.NormalizeDegrees(), angles1.z.NormalizeDegrees());

        // heuristic to determine a solution with angles closer to 0
        float h1 = Mathf.Abs(angles1.x) + Math.Abs(angles1.y) + Math.Abs(angles1.z);

        // alternative solution
        Vector3 angles2 = new Vector3((180f - angles1.x).NormalizeDegrees(), (angles1.y - 180f).NormalizeDegrees(), (angles1.z - 180f).NormalizeDegrees());
        float h2 = Mathf.Abs(angles2.x) + Mathf.Abs(angles2.y) + Math.Abs(angles2.z);

        return h1 <= h2 ? angles1 : angles2;
    }

}
