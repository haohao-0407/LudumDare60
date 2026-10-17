可复用白模 WhiteboxModule

这是一个独立、可实例化、可调整参数的白模预制体：正方形内接圆，箭尾在圆心，箭头指向圆周。
包内包含白模脚本、独立程序集、材质、着色器、参数面板和一个预制体；不需要 Input System。

导入与使用
1. 在目标 Unity 项目中选择 Assets > Import Package > Custom Package，选择 .unitypackage 文件并点击 Import。
2. 将 Assets/WhiteboxModule/Prefabs/WhiteboxModule.prefab 拖入场景。
3. 选中白模，在 Inspector 的 Assembly Module 组件中调整名称、Radius（半径）和 Port Angles（角度数组）。
4. 数组中每个数值对应一个箭头。0° 向上，90° 向右，180° 向下，270° 向左；顺时针为正。数组为空时默认一个 0° 箭头。
5. 多个实例可以分别设置不同角度。实例上的参数修改可使用 Undo 撤销；要修改所有实例的默认值，可在 Prefab Mode 中编辑原预制体。

代码调用示例
using UnityEngine;
using LudumDare60.AssemblyDemo;

public class SpawnWhitebox : MonoBehaviour
{
    // 将包内 WhiteboxModule.prefab 拖入此字段。
    [SerializeField] private AssemblyModule whiteboxPrefab;
    [SerializeField] private AssemblyModule target;

    private void Start()
    {
        AssemblyModule module = Instantiate(whiteboxPrefab, new Vector3(-2f, 0f, 0f), Quaternion.identity);
        module.SetAngles(30f, 90f);
        module.SetRadius(0.9f);
        module.RotationDegrees = 15f; // 整个白模顺时针旋转 15°。

        // 已靠近且方向满足误差条件时，调用一次进行精确对齐。
        if (target != null)
        {
            bool snapped = module.TrySnapTo(target);
            Debug.Log("本次是否吸附：" + snapped);
        }
    }
}

常用 API
SetAngles(params float[] angles)：修改本地箭头角度；SetAngles() 恢复默认 0° 箭头。
SetRadius(float radius)：修改半径并更新白模外观。
RotationDegrees：读取或设置整体世界旋转角度，0° 向上、顺时针为正。
Position：读取或设置白模中心的世界 XY 坐标。
PortCount、PortAngle(i)、PortDirection(i)、PortPosition(i)：读取接口数量、本地角度、世界方向和世界接口位置。
Rebuild()：重新生成外观；通过 SetAngles / SetRadius 修改参数时不需要再调用。
TrySnapTo(AssemblyModule target)：尝试移动和旋转当前白模，使合格接口精确共线、相向、端点重合；目标白模不移动。

吸附与使用范围
吸附容差固定为角度 5°、横向偏差 0.16、接口距离 0.12 Unity 世界单位。
TrySnapTo 只执行单次吸附对齐，不会自动持续搜索，也不建立持久连接关系。
键盘输入、鼠标选择、连接组整体移动、拆卸等交互属于原 Demo 的控制层，不包含在这个单个白模包内。
白模工作在 XY 平面。请使用 Radius 改尺寸，保持白模及父节点的 Transform Scale 为 (1, 1, 1)，避免绕 X/Y 轴旋转。
本包面向 Unity 6000.6.5f1 的 Built-in 与 URP 项目；不声明其他 Unity 版本或 HDRP 已兼容或已验证。
脚本命名空间沿用 LudumDare60.AssemblyDemo，以兼容原项目；这不意味着需要导入原 Demo。
如果调用方脚本也使用 asmdef，请在其 Assembly Definition References 中添加 WhiteboxModule.Runtime。
