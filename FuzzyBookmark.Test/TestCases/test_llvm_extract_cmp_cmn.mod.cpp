// Expect: 25
#include <llvm/CodeGen/SelectionDAG.h>

namespace llvm {

class SDValue;
class CCValAssign;
class SelectionDAG;

bool isCMN(SDValue V, const CCValAssign& CC, SelectionDAG& DAG) {
    return false;
}

int getCmpOperandFoldingProfit(SDValue V, bool IsLHS) {
    return 0;
}

int getCmpOrCmnOperandFoldingProfit(SDValue V, const CCValAssign& CC, SelectionDAG& DAG) {
    return getCmpOperandFoldingProfit(V, true) + (isCMN(V, CC, DAG) ? 1 : 0);
}

SDValue tryFoldCMN(SDValue LHS, SDValue RHS, const CCValAssign& CC, SelectionDAG& DAG) {
    if (getCmpOrCmnOperandFoldingProfit(LHS, CC, DAG) >
        getCmpOrCmnOperandFoldingProfit(RHS, CC, DAG)) {
        return LHS;
    }
    return RHS;
}

}

int main()
{
    return 0;
}
