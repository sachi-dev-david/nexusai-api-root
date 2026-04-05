using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NexusAI.Api.Data.Entities;

/// <summary>
/// 對應 mldatabase.baseparameters
/// TYPE: MAT=材料, SUR=表面處理, HEAT=熱處理, DIM=尺寸公差, GEO=型位公差, SURR=表面粗糙度
/// </summary>
[Table("baseparameters")]
public class BaseParameterEntity
{
    [Key]
    [Column("id"), MaxLength(64)]
    public required string Id { get; set; }

    /// <summary>MAT / SUR / HEAT / DIM / GEO / SURR</summary>
    [Column("TYPE"), MaxLength(10)]
    public required string Type { get; set; }

    [Column("NO")]
    public int No { get; set; }

    [Column("NAME"), MaxLength(200)]
    public required string Name { get; set; }

    [Column("EN_NAME"), MaxLength(200)]
    public string EnName { get; set; } = "";

    /// <summary>單價（材料: 元/kg，表面處理/熱處理: 元/kg）</summary>
    [Column("UnitPrice", TypeName = "decimal(12,4)")]
    public decimal UnitPrice { get; set; }

    /// <summary>密度 kg/m³（只有 MAT 有値）</summary>
    [Column("Density", TypeName = "decimal(12,4)")]
    public decimal Density { get; set; }
}
