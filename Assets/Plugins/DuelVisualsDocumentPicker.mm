#import <UIKit/UIKit.h>
#import <UniformTypeIdentifiers/UniformTypeIdentifiers.h>

extern "C" UIViewController *UnityGetGLViewController(void);
typedef void (*YgoZipPicked)(int, const char *);

@interface YgoDuelVisualsPicker : NSObject <UIDocumentPickerDelegate>
@property(nonatomic, assign) YgoZipPicked completion;
@end

@implementation YgoDuelVisualsPicker

- (void)finish:(int)result path:(NSString *)path
{
    YgoZipPicked completion = self.completion;
    self.completion = NULL;
    if (completion) completion(result, path.UTF8String);
}

- (void)documentPicker:(UIDocumentPickerViewController *)controller didPickDocumentsAtURLs:(NSArray<NSURL *> *)urls
{
    NSURL *copy = urls.firstObject;
    if (!copy.isFileURL)
    {
        [self finish:-1 path:nil];
        return;
    }

    NSString *filename = [NSString stringWithFormat:@"ygo-duel-visuals-%@.zip", [NSUUID UUID].UUIDString];
    NSURL *temporary = [NSURL fileURLWithPath:[NSTemporaryDirectory() stringByAppendingPathComponent:filename]];
    // The picker hands us a copy. Only the app-owned temporary file is passed to Unity.
    if (![[NSFileManager defaultManager] moveItemAtURL:copy toURL:temporary error:NULL])
    {
        [self finish:-1 path:nil];
        return;
    }
    [self finish:1 path:temporary.path];
}

- (void)documentPickerWasCancelled:(UIDocumentPickerViewController *)controller
{
    [self finish:0 path:nil];
}

@end

extern "C" void YgoPickDuelVisualsZip(YgoZipPicked completion)
{
    if (!completion) return;
    dispatch_async(dispatch_get_main_queue(), ^{
        static YgoDuelVisualsPicker *delegate = [YgoDuelVisualsPicker new];
        UIViewController *presenter = UnityGetGLViewController();
        while (presenter.presentedViewController) presenter = presenter.presentedViewController;
        if (!presenter.view.window || delegate.completion)
        {
            completion(-1, NULL);
            return;
        }

        UIDocumentPickerViewController *picker = [[UIDocumentPickerViewController alloc]
            initForOpeningContentTypes:@[UTTypeZIP] asCopy:YES];
        picker.delegate = delegate;
        picker.allowsMultipleSelection = NO;
        delegate.completion = completion;
        [presenter presentViewController:picker animated:YES completion:nil];
#if !__has_feature(objc_arc)
        [picker release];
#endif
    });
}
