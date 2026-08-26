#ifndef TOURNAMENT_PARALLELOGRAM_BORDER_FS
#define TOURNAMENT_PARALLELOGRAM_BORDER_FS

#undef HIGH_PRECISION_VERTEX
#define HIGH_PRECISION_VERTEX

#include "sh_Utils.h"
#include "sh_Masking.h"

layout(location = 2) in highp vec2 v_TexCoord;

layout(std140, set = 0, binding = 0) uniform m_TournamentParallelogramBorderParameters
{
    highp float borderThickness;
};

layout(location = 0) out vec4 o_Colour;

highp float signedDistanceToRect(highp vec2 point, highp vec2 minCorner, highp vec2 maxCorner)
{
    highp vec2 distanceToEdges = max(minCorner - point, point - maxCorner);
    highp vec2 outside = max(distanceToEdges, vec2(0.0));
    highp float outsideDistance = length(outside);
    highp float insideDistance = max(distanceToEdges.x, distanceToEdges.y);

    return outsideDistance + min(insideDistance, 0.0);
}

highp float coverage(highp float signedDistance)
{
    highp float smoothingWidth = max(fwidth(signedDistance), 0.0001);
    return 1.0 - smoothstep(-smoothingWidth, smoothingWidth, signedDistance);
}

void main(void)
{
    highp vec2 minCorner = min(v_TexRect.xy, v_TexRect.zw);
    highp vec2 maxCorner = max(v_TexRect.xy, v_TexRect.zw);
    highp vec2 borderInset = min(vec2(borderThickness), (maxCorner - minCorner) * 0.5);
    highp float outerCoverage = coverage(signedDistanceToRect(v_TexCoord, minCorner, maxCorner));
    highp float innerCoverage = coverage(signedDistanceToRect(v_TexCoord, minCorner + borderInset, maxCorner - borderInset));
    lowp float borderCoverage = clamp(outerCoverage - innerCoverage, 0.0, 1.0);

    o_Colour = getRoundedColor(vec4(v_Colour.rgb, v_Colour.a * borderCoverage), v_TexCoord);
}

#endif
