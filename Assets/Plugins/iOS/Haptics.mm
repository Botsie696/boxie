#import <UIKit/UIKit.h>

static UISelectionFeedbackGenerator *sSelection;
static UIImpactFeedbackGenerator *sLight;
static UIImpactFeedbackGenerator *sMedium;

extern "C" {
    void _HapticSelection(void)
    {
        if (!sSelection) sSelection = [[UISelectionFeedbackGenerator alloc] init];
        [sSelection selectionChanged];
        [sSelection prepare];
    }

    void _HapticLightImpact(void)
    {
        if (!sLight) sLight = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleLight];
        [sLight impactOccurred];
        [sLight prepare];
    }

    void _HapticMediumImpact(void)
    {
        if (!sMedium) sMedium = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleMedium];
        [sMedium impactOccurred];
        [sMedium prepare];
    }
}
