# DateTimeHighResWin7

Here I present a possible solution (or at least hopefully a pretty good polyfill) for those who want the precision of the new GetSystemTimePreciseAsFileTime API but have OS versions that do not support it. The GetSystemTimePreciseAsFileTime API is only available in Windows 8 or later or Windows Server 2012 or later. Many people still use Windows 7 and this will probably remain true for several years.

Therefore, DateTimeHighResWin7 uses the high-resolution QueryPerformanceCounter API along with the low-resolution system timer to estimate a high-precision date/time. While others have implemented similar concepts on the surface, my code not only provides a close approximation to real time but it also ensures monotonically-increasing values (for calls on the same thread).
