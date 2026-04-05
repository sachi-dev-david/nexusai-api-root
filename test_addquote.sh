#!/bin/bash
# 快速測試 AddQuote API 的腳本

# 配置
API_URL="http://localhost:5000"
JWT_TOKEN="${1:-YOUR_JWT_TOKEN_HERE}"  # 第一個參數傳入 JWT Token
QUOTE_NAME="${2:-Q_REQUEST}"
QUOTE_FILE="${3:-A001}"
CUSTOMER="${4:-HZD}"
FILE_PATH="${5:-D:\\Models\\part001.stp}"

# 顏色定義
GREEN='\033[0;32m'
BLUE='\033[0;34m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

echo -e "${BLUE}========================================${NC}"
echo -e "${BLUE}    AddQuote API 快速測試工具${NC}"
echo -e "${BLUE}========================================${NC}"
echo ""

# 檢查 JWT Token
if [ "$JWT_TOKEN" == "YOUR_JWT_TOKEN_HERE" ]; then
    echo -e "${YELLOW}⚠️  請提供 JWT Token!${NC}"
    echo ""
    echo "使用方法:"
    echo "  ./test_addquote.sh <JWT_TOKEN> [quote_name] [file_name] [customer] [file_path]"
    echo ""
    echo "範例:"
    echo "  ./test_addquote.sh eyJhbGciOiJIUzI1NiIs... Q_SACH A001 HZD D:\\\\Models\\\\part001.stp"
    echo ""
    exit 1
fi

echo -e "${GREEN}✓ 配置信息:${NC}"
echo "  API URL: $API_URL"
echo "  報價名: $QUOTE_NAME"
echo "  文件號: $QUOTE_FILE"
echo "  公司名: $CUSTOMER"
echo "  STP 路徑: $FILE_PATH"
echo ""

# 構建請求體
REQUEST_BODY=$(cat <<EOF
{
  "quoteName": "$QUOTE_NAME",
  "quoteFileName": "$QUOTE_FILE",
  "customerName": "$CUSTOMER",
  "filePath": "$FILE_PATH",
  "materialName": "316不鏽鋼",
  "surfaceTreatment": "磨砂",
  "heatTreatment": "無"
}
EOF
)

echo -e "${BLUE}發送請求...${NC}"
echo ""

# 發送請求
RESPONSE=$(curl -s -X POST "$API_URL/api/quotes/add" \
  -H "Authorization: Bearer $JWT_TOKEN" \
  -H "Content-Type: application/json" \
  -w "\n%{http_code}" \
  -d "$REQUEST_BODY")

# 分離響應體和狀態碼
HTTP_CODE=$(echo "$RESPONSE" | tail -n1)
RESPONSE_BODY=$(echo "$RESPONSE" | head -n-1)

# 解析和顯示結果
echo -e "${BLUE}HTTP 狀態碼: $HTTP_CODE${NC}"
echo ""

if [ "$HTTP_CODE" == "200" ]; then
    echo -e "${GREEN}✓ 請求成功!${NC}"
    echo ""
    echo "響應:"
    echo "$RESPONSE_BODY" | python3 -m json.tool 2>/dev/null || echo "$RESPONSE_BODY"
elif [ "$HTTP_CODE" == "400" ]; then
    echo -e "${YELLOW}⚠️  請求參數錯誤${NC}"
    echo ""
    echo "響應:"
    echo "$RESPONSE_BODY" | python3 -m json.tool 2>/dev/null || echo "$RESPONSE_BODY"
elif [ "$HTTP_CODE" == "401" ]; then
    echo -e "${YELLOW}⚠️  未授權 - JWT Token 無效${NC}"
    echo ""
    echo "請檢查:"
    echo "  1. JWT Token 是否正確"
    echo "  2. Token 是否已過期"
    echo ""
    echo "響應:"
    echo "$RESPONSE_BODY"
elif [ "$HTTP_CODE" == "500" ]; then
    echo -e "${YELLOW}⚠️  伺服器錯誤${NC}"
    echo ""
    echo "可能的原因:"
    echo "  1. 特徵辨識 API 未運行 (localhost:8801)"
    echo "  2. ML 模型 API 未運行 (localhost:8800)"
    echo "  3. STP 文件路徑無效"
    echo ""
    echo "響應:"
    echo "$RESPONSE_BODY" | python3 -m json.tool 2>/dev/null || echo "$RESPONSE_BODY"
else
    echo -e "${YELLOW}⚠️  未預期的狀態碼: $HTTP_CODE${NC}"
    echo ""
    echo "響應:"
    echo "$RESPONSE_BODY"
fi

echo ""
echo -e "${BLUE}========================================${NC}"

