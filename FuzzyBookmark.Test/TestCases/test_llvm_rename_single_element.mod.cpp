// Expect: 9
#include <clang/AST/Type.h>

namespace clang {

class SystemZABIInfo {
public:
    QualType getSingleElementType(QualType Ty) const;
};

QualType SystemZABIInfo::getSingleElementType(QualType Ty) const {
    return Ty;
}

}

int main()
{
    return 0;
}
