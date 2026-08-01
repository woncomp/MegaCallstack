// Bookmark: 12
#include <vector>

namespace clang {

class Pointer;

bool isUsefulPtr(const Pointer& Ptr) {
    return Ptr.isValid();
}

class EvaluationResult {
public:
    void collectBlocks(const Pointer& Ptr) {
        if (!isUsefulPtr(Pointer(Ptr))) {
            return;
        }
        if (Ptr.isBlock()) {
            blocks.push_back(Ptr);
        }
    }

    std::vector<Pointer> blocks;
};

}

int main()
{
    return 0;
}
