using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NexusAI.Api.Data.Entities;

/// <summary>
/// 對應 mldatabase.machinelearning_quatation
/// </summary>
[Table("machinelearning_quatation")]
public class QuotationEntity
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    /// <summary>報價單名稱，格式：Q_{專案名稱}</summary>
    [Column("quote_name"), Required, MaxLength(200)]
    public required string QuoteName { get; set; }

    /// <summary>報價檔案名稱，格式：A001、B001 …</summary>
    [Column("quote_file_name"), Required, MaxLength(100)]
    public required string QuoteFileName { get; set; }

    [Column("customer_name"), Required, MaxLength(200)]
    public required string CustomerName { get; set; }

    [Column("material_cost", TypeName = "decimal(18,2)")]
    public decimal MaterialCost { get; set; }

    [Column("processing_cost", TypeName = "decimal(18,2)")]
    public decimal ProcessingCost { get; set; }

    [Column("surface_treatment_cost", TypeName = "decimal(18,2)")]
    public decimal SurfaceTreatmentCost { get; set; }

    [Column("heat_treatment_cost", TypeName = "decimal(18,2)")]
    public decimal HeatTreatmentCost { get; set; }

    [Column("material_name"), MaxLength(200)]
    public string? MaterialName { get; set; }

    [Column("surface_treatment"), MaxLength(200)]
    public string? SurfaceTreatment { get; set; }

    [Column("heat_treatment"), MaxLength(200)]
    public string? HeatTreatment { get; set; }

    /// <summary>總計成本 = 材料費 + 加工費 + 表面處理費 + 熱處理費（DB 計算欄位）</summary>
    [Column("total_cost", TypeName = "decimal(18,2)")]
    public decimal TotalCost { get; set; }

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }
}
