//
//  MainPresenter.swift
//  test_for_Cuckoo
//
//  Created by wada on 2020/12/08.
//  Copyright © 2020 和田樹拓. All rights reserved.
//

import Foundation
protocol PresenterProtocol {
    
}

struct MainPresenter: PresenterProtocol {
    var view: ViewController
    init(vc: ViewController) {
        self.view = vc
    }
    
}
