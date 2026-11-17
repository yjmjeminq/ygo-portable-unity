#import <Foundation/Foundation.h>
#import <Security/Security.h>
#include <stdlib.h>
#include <string.h>

static NSDictionary *YgoCloudQuery()
{
    return @{
        (__bridge id)kSecClass: (__bridge id)kSecClassGenericPassword,
        (__bridge id)kSecAttrService: @"com.ygoportable.lotd.cloud-save",
        (__bridge id)kSecAttrAccount: @"github"
    };
}

extern "C" char *YgoCloudCopyToken()
{
    @autoreleasepool
    {
        NSMutableDictionary *query = [NSMutableDictionary dictionaryWithDictionary:YgoCloudQuery()];
        query[(__bridge id)kSecReturnData] = @YES;
        query[(__bridge id)kSecMatchLimit] = (__bridge id)kSecMatchLimitOne;
        CFTypeRef result = NULL;
        if (SecItemCopyMatching((__bridge CFDictionaryRef)query, &result) != errSecSuccess) return NULL;
        NSData *data = (__bridge NSData *)result;
        char *token = static_cast<char *>(calloc(data.length + 1, 1));
        if (token) memcpy(token, data.bytes, data.length);
        CFRelease(result);
        return token;
    }
}

extern "C" int YgoCloudSetToken(const char *token)
{
    @autoreleasepool
    {
        NSDictionary *query = YgoCloudQuery();
        if (!token)
        {
            OSStatus status = SecItemDelete((__bridge CFDictionaryRef)query);
            return status == errSecItemNotFound ? errSecSuccess : status;
        }

        NSData *data = [NSData dataWithBytes:token length:strlen(token)];
        NSDictionary *values = @{ (__bridge id)kSecValueData: data };
        OSStatus status = SecItemUpdate((__bridge CFDictionaryRef)query, (__bridge CFDictionaryRef)values);
        if (status != errSecItemNotFound) return status;
        NSMutableDictionary *entry = [NSMutableDictionary dictionaryWithDictionary:query];
        entry[(__bridge id)kSecValueData] = data;
        entry[(__bridge id)kSecAttrAccessible] = (__bridge id)kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly;
        return SecItemAdd((__bridge CFDictionaryRef)entry, NULL);
    }
}

extern "C" void YgoCloudFreeToken(char *token)
{
    free(token);
}
