// Bookmark: 9
#include <clang/AST/Type.h>

namespace clang {

class SystemZABIInfo {
public:
    QualType GetSingleElementType(QualType Ty) const;
};

QualType SystemZABIInfo::GetSingleElementType(QualType Ty) const {
    return Ty;
}

}

int main()
{
    return 0;
}
