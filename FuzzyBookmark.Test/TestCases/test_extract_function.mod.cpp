// Expect: 11
#include <iostream>

namespace App
{
    class Processor
    {
    public:
        int Compute()
        {
            int c = Multiply(3, 5);
            int d = c + 1;
            return d;
        }

        int Multiply(int a, int b)
        {
            int c = a * b;
            return c;
        }
    };
}
