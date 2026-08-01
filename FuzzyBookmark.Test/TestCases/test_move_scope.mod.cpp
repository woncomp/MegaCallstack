// Expect: 19
#include <iostream>

namespace App
{
    class Processor
    {
    public:
        void Run()
        {
            int value = 42;
            std::cout << value << std::endl;
        }
    };

    class Helper
    {
    public:
        void DoHelper()
        {
        }
    };
}
