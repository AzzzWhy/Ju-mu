# 句幕反向导入示例：一个文件，两个角色，嵌套菜单和跨文本跳转。
define lin = Character("林夏")
define chen = Character("陈默")

label start:
    "雨停的时候，旧档案馆的灯还亮着。"
    lin "门口这张纸上写着：‘请勿进入’，你还要继续吗？"
    chen "先听听你的意见。"
    menu:
        "接下来怎么做？"
        "推开档案馆的门":
            lin "走吧，我们一起进去。"
            jump archive
        "留在门外":
            menu:
                "再等等":
                    "两个人在檐下听着滴水声。"
                "结束调查":
                    chen "今天就到这里。"
                    return
    "门外的风渐渐平静。"
    return

label archive:
    "桌上躺着一本没有署名的日记。"
    chen "这段英文说：Don't forget the key."
    lin "钥匙？也许答案就在最后一页。"
    menu:
        "翻到最后一页":
            jump ending
        "回到门口重新选择":
            jump start
    return

label ending:
    "纸页里夹着一把银色的小钥匙。"
    lin "找到了。故事还没有结束。"
    return
