using System;
using UnityEngine;

/*
 * DataModels.cs
 *
 * 作者:阿灿
 * 创建日期: 2023-06-15
 *
 * 描述:
 * 定义所有数据模型和枚举类型
 */

[System.Serializable]
public class ModelCheckConfig
{
    public int maxTriangles = 10000;
    public int maxMaterials = 2;
    public int maxBones = 60;
    public int maxTextureSize = 4096;
    public float pivotTolerance = 1f; // 轴心偏移容差
    public bool checkChildrenNaming = true;
    public bool checkUVOverlap = false;
    public bool checkLightmapUV = false;
    public bool checkPivotOffset = true; // 是否检查轴心偏移

    // 新增：材质使用分析配置
    public bool checkMaterialUsage = true; // 是否检查材质使用效率
    public int minDrawCallBudget = 200; // 最小DrawCall预算
    public int maxDrawCallBudget = 700; // 最大DrawCall预算

    // 新增：UV拉伸检测配置
    public bool checkUVStretch = true; // 是否检查UV拉伸
    public float uvStretchThreshold = 5f; // UV拉伸比阈值
}

public enum IssueType
{
    TriangleCount, // 三角面数问题
    MaterialCount, // 材质数量问题
    MissingMaterial, // 材质引用丢失
    MeshTopology, // 网格拓扑问题
    Naming, // 命名规范问题
    Transform, // 变换设置问题
    EmptyNode, // 空节点问题
    TextureSize, // 贴图尺寸问题
    TexturePowerOfTwo, // 贴图尺寸规范
    TextureImportSettings, // 贴图导入设置
    UVMapping, // UV映射问题
    LightmapUV, // Lightmap UV问题
    ImportSettings, // 导入设置问题
    BoneCount, // 骨骼数量问题
    MissingBone, // 骨骼引用丢失
    Animation, // 动画设置问题
    PivotOffset, // 轴心偏移问题
    MaterialUsage, // 材质使用效率问题
    UVStretch // 新增：UV拉伸问题
}

[System.Serializable]
public class ModelIssue
{
    public GameObject gameObject;
    public string modelName;
    public IssueType type;
    public string description;
    public string suggestion;
    public PivotOffsetInfo pivotOffsetInfo; // 轴心偏移详细信息
    public UVStretchInfo uvStretchInfo;

    public class UVStretchInfo
    {
        public float maxStretch;
        public float texelDensity;
    }
}

[System.Serializable]
public class PivotOffsetInfo
{
    public Vector3 position; // 游戏对象的世界坐标位置
    public Vector3 offset; // 偏移向量
    public float distance; // 偏移距离
}
