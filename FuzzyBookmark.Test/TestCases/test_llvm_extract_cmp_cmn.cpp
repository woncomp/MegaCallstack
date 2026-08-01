// Bookmark: 23
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

SDValue tryFoldCMN(SDValue LHS, SDValue RHS, const CCValAssign& CC, SelectionDAG& DAG) {
    bool LHSIsCMN = isCMN(LHS, CC, DAG);
    bool RHSIsCMN = isCMN(RHS, CC, DAG);
    SDValue TheLHS = LHSIsCMN ? LHS.getOperand(1) : LHS;
    SDValue TheRHS = RHSIsCMN ? RHS.getOperand(1) : RHS;
    if (getCmpOperandFoldingProfit(TheLHS, true) + (LHSIsCMN ? 1 : 0) >
        getCmpOperandFoldingProfit(TheRHS, true) + (RHSIsCMN ? 1 : 0)) {
        return TheLHS;
    }
    return TheRHS;
}

}

int main()
{
    return 0;
}
